using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

[assembly: InternalsVisibleTo("SmartGym.Tests")]

namespace SmartGym.Data.Db;

/// <summary>
/// Aplica el schema (scripts/schema_smart_gym.sql) en cada arranque. El script
/// es idempotente por diseño (CREATE IF NOT EXISTS + seed con INSERT OR IGNORE /
/// WHERE NOT EXISTS), así que ejecutarlo siempre es lo que permite que las
/// tablas nuevas del catálogo lleguen a bases de datos ya creadas sin un
/// mecanismo de migraciones. Costo: milisegundos de DDL IF NOT EXISTS.
///
/// Detección automática de columnas faltantes (agregado 2026-08-26):
/// Antes de ejecutar el script, compara el schema esperado (parseado del .sql)
/// contra la BD real. Si detecta columnas faltantes en tablas existentes, crea
/// un backup automático y las agrega con ALTER TABLE ADD COLUMN. Solo agrega
/// columnas nullable o con DEFAULT; las NOT NULL sin DEFAULT se omiten con log.
///
/// Resiliencia ante BD legacy (caso real en producción, ver docs/
/// migracion-dotnet/10-inicializacion-schema-en-arranque.md caso 3):
/// 1. Los CREATE INDEX sobre columnas que no existen aún en la BD destino se
///    OMITEN con log (nunca tumban el arranque).
/// 2. Si el batch completo falla por cualquier otro motivo, se reintenta
///    sentencia por sentencia aplicando lo que sí pueda y registrando cada
///    fallo — una sentencia rota jamás detiene el inicio completo.
/// </summary>
public static class DbInitializer
{
    private const string SchemaResource = "SmartGym.Data.Scripts.schema_smart_gym.sql";
    private const int MaxBackups = 5;
    private const long MaxLogBytes = 5 * 1024 * 1024; // 5 MB

    /// <summary>
    /// Hook de diagnóstico: MauiProgram lo cablea al log de archivo para que
    /// ninguna omisión/fallo pase desapercibido en instalaciones reales.
    /// </summary>
    public static Action<string>? LogWarning { get; set; }

    public static void Initialize(string dbPath)
    {
        Initialize(dbPath, ReadSchemaScript());
    }

    /// <summary>Versión inyectable para pruebas (mismo flujo exacto).</summary>
    internal static void Initialize(string dbPath, string script)
    {
        // Paso 0: detectar y agregar columnas faltantes (con backup previo).
        AgregarColumnasFaltantes(dbPath, script);

        using var connection = ConnectionFactory.Open(dbPath);

        var filtrado = OmitirIndicesNoEjecutables(script, connection);

        try
        {
            EjecutarBatch(connection, filtrado);
        }
        catch (SqliteException ex)
        {
            Log($"El schema batch falló ({ex.Message}) — reintentando sentencia por sentencia.");
            EjecutarPorSentencia(connection, filtrado);
        }
    }

    // =====================================================================
    // Detección y agregado automático de columnas faltantes
    // =====================================================================

    internal sealed record ColumnaEsperada(string Nombre, string Tipo, bool EsNotNull, string? DefaultExpr);
    internal sealed record ColumnaFaltante(string Tabla, string Nombre, string Tipo, bool EsNotNull, string? DefaultExpr);

    /// <summary>
    /// Paso previo al schema: detecta columnas faltantes en tablas existentes
    /// y las agrega con backup automático. Si el backup falla, no toca nada.
    /// </summary>
    internal static void AgregarColumnasFaltantes(string dbPath, string script)
    {
        if (!File.Exists(dbPath))
        {
            return; // BD nueva — el CREATE TABLE trae todo el schema.
        }

        var esperado = ParsearSchemaEsperado(script);
        var faltantes = DetectarColumnasFaltantes(dbPath, esperado);

        if (faltantes.Count == 0)
        {
            return;
        }

        Log($"Detectadas {faltantes.Count} columna(s) faltante(s): " +
            string.Join(", ", faltantes.Select(f => $"{f.Tabla}.{f.Nombre}")));

        // Backup obligatorio antes de tocar el schema.
        var backupPath = CrearBackup(dbPath);
        if (backupPath is null)
        {
            Log("BACKUP FALLÓ — se omite el agregado de columnas por seguridad.");
            return;
        }

        Log($"Backup creado: {backupPath}");
        LimpiarBackupsAntiguos(dbPath);

        AgregarColumnas(dbPath, faltantes);
    }

    /// <summary>
    /// Parsea los bloques CREATE TABLE del script SQL y extrae la definición
    /// esperada de cada columna (nombre, tipo, NOT NULL, DEFAULT).
    /// </summary>
    internal static Dictionary<string, List<ColumnaEsperada>> ParsearSchemaEsperado(string script)
    {
        var resultado = new Dictionary<string, List<ColumnaEsperada>>(StringComparer.OrdinalIgnoreCase);

        var lines = script.Split('\n');
        string? tablaActual = null;
        var columnas = new List<ColumnaEsperada>();
        var parenDepth = 0;
        var enCreateTable = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            if (!enCreateTable)
            {
                // Detectar inicio de CREATE TABLE IF NOT EXISTS.
                var m = Regex.Match(line,
                    @"CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+(\S+)\s*\(",
                    RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    tablaActual = m.Groups[1].Value.Trim().Trim('"', '[', ']', '`');
                    columnas = new List<ColumnaEsperada>();
                    parenDepth = 1;
                    enCreateTable = true;

                    // Si hay contenido después del '(' en la misma línea, procesarlo.
                    var afterOpen = line.Substring(m.Index + m.Length);
                    if (!string.IsNullOrWhiteSpace(afterOpen))
                    {
                        ProcesarLineaColumna(afterOpen.Trim().TrimEnd(','), columnas);
                    }
                }
                continue;
            }

            // Dentro de un bloque CREATE TABLE.
            foreach (var ch in line)
            {
                if (ch == '(') parenDepth++;
                else if (ch == ')') parenDepth--;
            }

            if (parenDepth <= 0)
            {
                // Fin del bloque CREATE TABLE.
                if (tablaActual is not null)
                {
                    resultado[tablaActual] = columnas;
                }
                enCreateTable = false;
                tablaActual = null;
                continue;
            }

            // Procesar línea como posible columna.
            ProcesarLineaColumna(line.Trim().TrimEnd(','), columnas);
        }

        return resultado;
    }

    private static void ProcesarLineaColumna(string line, List<ColumnaEsperada> columnas)
    {
        // Strip inline SQL comments (-- ...) and trailing comma.
        var commentIdx = line.IndexOf("--", StringComparison.Ordinal);
        if (commentIdx >= 0)
        {
            line = line.Substring(0, commentIdx);
        }
        line = line.Trim().TrimEnd(',');

        if (string.IsNullOrWhiteSpace(line) || line == ")")
        {
            return;
        }

        var upper = line.ToUpperInvariant();
        if (upper.StartsWith("PRIMARY KEY") ||
            upper.StartsWith("FOREIGN KEY") ||
            upper.StartsWith("UNIQUE") ||
            upper.StartsWith("CHECK") ||
            upper.StartsWith("CONSTRAINT"))
        {
            return;
        }

        var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            return;
        }

        var nombre = tokens[0].Trim('"', '[', ']', '`');
        var tipo = tokens[1].Trim('"', '[', ']', '`').TrimEnd(',');

        var resto = string.Join(' ', tokens.Skip(2));
        var esNotNull = resto.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase);

        string? defaultExpr = null;
        var defaultIdx = resto.IndexOf("DEFAULT", StringComparison.OrdinalIgnoreCase);
        if (defaultIdx >= 0)
        {
            var afterDefault = resto.Substring(defaultIdx + 7).TrimStart();
            defaultExpr = ExtraerDefaultValue(afterDefault);
        }

        columnas.Add(new ColumnaEsperada(nombre, tipo, esNotNull, defaultExpr));
    }

    /// <summary>Extrae el valor DEFAULT de una cadena que empieza justo después de DEFAULT.</summary>
    private static string ExtraerDefaultValue(string afterDefault)
    {
        if (string.IsNullOrWhiteSpace(afterDefault))
        {
            return "NULL";
        }

        // Si empieza con comilla simple: string literal.
        if (afterDefault.StartsWith("'"))
        {
            var end = afterDefault.IndexOf('\'', 1);
            return end > 0 ? afterDefault.Substring(0, end + 1) : afterDefault.Split(' ')[0];
        }

        // Si es una función como strftime(...): tomar hasta la coma o fin de tokens.
        if (afterDefault.StartsWith("strftime", StringComparison.OrdinalIgnoreCase) ||
            afterDefault.StartsWith("(", StringComparison.OrdinalIgnoreCase))
        {
            var closeParen = 0;
            for (var i = 0; i < afterDefault.Length; i++)
            {
                if (afterDefault[i] == '(') closeParen++;
                else if (afterDefault[i] == ')')
                {
                    closeParen--;
                    if (closeParen == 0)
                    {
                        return afterDefault.Substring(0, i + 1);
                    }
                }
            }
            return afterDefault.Split(' ')[0];
        }

        // Token simple: número, NULL, etc.
        return afterDefault.Split(' ', ',', ')')[0];
    }

    /// <summary>
    /// Compara el schema esperado contra la BD real y retorna las columnas
    /// que faltan en tablas ya existentes.
    /// </summary>
    internal static List<ColumnaFaltante> DetectarColumnasFaltantes(
        string dbPath, Dictionary<string, List<ColumnaEsperada>> esperado)
    {
        var faltantes = new List<ColumnaFaltante>();

        using var conn = ConnectionFactory.Open(dbPath);

        foreach (var (tabla, columnasEsperadas) in esperado)
        {
            if (!TablaExiste(conn, tabla))
            {
                continue; // Tabla nueva — la crea el script completo después.
            }

            // Obtener columnas reales de la BD.
            var reales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info('{tabla.Replace("'", "''")}')";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    reales.Add(reader.GetString(1));
                }
            }

            foreach (var col in columnasEsperadas)
            {
                if (reales.Contains(col.Nombre))
                {
                    continue;
                }

                // Regla: solo agregar nullable o con DEFAULT.
                // NOT NULL sin DEFAULT → omitir con log (requiere backfill manual).
                if (col.EsNotNull && col.DefaultExpr is null)
                {
                    Log($"Columna {tabla}.{col.Nombre} es NOT NULL sin DEFAULT — se omite (requiere migración manual)");
                    continue;
                }

                faltantes.Add(new ColumnaFaltante(tabla, col.Nombre, col.Tipo, col.EsNotNull, col.DefaultExpr));
            }
        }

        return faltantes;
    }

    /// <summary>
    /// Crea un backup del archivo .db (+ sidecars -wal/-shm) con timestamp.
    /// Retorna la ruta del backup o null si falla.
    /// </summary>
    internal static string? CrearBackup(string dbPath)
    {
        try
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var backup = $"{dbPath}.bak_{stamp}";
            File.Copy(dbPath, backup, overwrite: true);

            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                var sidecar = dbPath + suffix;
                if (File.Exists(sidecar))
                {
                    File.Copy(sidecar, backup + suffix, overwrite: true);
                }
            }

            return backup;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Mantiene solo los últimos MaxBackups backups, borra los más antiguos.</summary>
    internal static void LimpiarBackupsAntiguos(string dbPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

            var baseName = Path.GetFileName(dbPath);
            var backups = Directory.GetFiles(dir, $"{baseName}.bak_*")
                .OrderByDescending(f => f)
                .ToList();

            // El nombre del backup incluye el timestamp, así que ordenar
            // alfabéticamente descendente equivale a ordenar por fecha descendente.
            foreach (var old in backups.Skip(MaxBackups))
            {
                File.Delete(old);
                // Borrar sidecars si existen.
                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    if (File.Exists(old + suffix))
                    {
                        File.Delete(old + suffix);
                    }
                }
            }
        }
        catch
        {
            // Error de limpieza no es crítico — los backups se acumulan pero
            // la app sigue funcionando.
        }
    }

    /// <summary>
    /// Ejecuta los ALTER TABLE ADD COLUMN para cada columna faltante.
    /// Cada ALTER va en su propia conexión; si uno falla se loggea y se continúa.
    /// </summary>
    internal static void AgregarColumnas(string dbPath, List<ColumnaFaltante> faltantes)
    {
        foreach (var col in faltantes)
        {
            try
            {
                using var conn = ConnectionFactory.Open(dbPath);
                using var cmd = conn.CreateCommand();

                var defaultClause = col.DefaultExpr is not null ? $" DEFAULT {col.DefaultExpr}" : "";
                var nullClause = col.EsNotNull ? " NOT NULL" : "";
                cmd.CommandText = $"ALTER TABLE {col.Tabla} ADD COLUMN {col.Nombre} {col.Tipo}{nullClause}{defaultClause}";
                cmd.ExecuteNonQuery();

                Log($"Columna agregada: {col.Tabla}.{col.Nombre} ({col.Tipo}{nullClause}{defaultClause})");
            }
            catch (SqliteException ex)
            {
                Log($"Error al agregar {col.Tabla}.{col.Nombre}: {ex.Message} — se omite");
            }
        }
    }

    // =====================================================================
    // Rotación del log de diagnóstico
    // =====================================================================

    /// <summary>
    /// Rota el archivo de log si supera MaxLogBytes: renombra a .old y crea uno nuevo.
    /// </summary>
    public static void RotarLogSiNecesario(string logPath)
    {
        try
        {
            if (!File.Exists(logPath))
            {
                return;
            }

            var info = new FileInfo(logPath);
            if (info.Length <= MaxLogBytes)
            {
                return;
            }

            var oldPath = logPath + ".old";
            if (File.Exists(oldPath))
            {
                File.Delete(oldPath);
            }
            File.Move(logPath, oldPath);
        }
        catch
        {
            // Error de rotación no es crítico.
        }
    }

    private static void EjecutarBatch(SqliteConnection connection, string script)
    {
        using var tx = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = script;
        command.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>
    /// Aplica sentencia por sentencia. Una sentencia que falla se registra y se
    /// omite; el resto del schema sigue aplicándose.
    /// </summary>
    private static void EjecutarPorSentencia(SqliteConnection connection, string script)
    {
        var statements = SplitStatements(script);
        foreach (var statement in statements)
        {
            if (string.IsNullOrWhiteSpace(statement))
            {
                continue;
            }

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }
            catch (SqliteException ex)
            {
                var resumen = statement.TrimStart();
                Log($"Sentencia de schema omitida por error: {ex.Message} — «{resumen.Substring(0, Math.Min(120, resumen.Length))}»");
            }
        }
    }

    /// <summary>True si la tabla existe ya en la BD destino.</summary>
    private static bool TablaExiste(SqliteConnection connection, string tabla)
    {
        using var cmd = connection.CreateCommand();
        // Nombre de tabla ya validado contra el propio script del sistema.
        cmd.CommandText = $"SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='{tabla.Replace("'", "''")}'";
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    /// <summary>
    /// Comenta todo CREATE INDEX cuya tabla/columnas no existan todavía en la
    /// BD destino (PRAGMA table_info). Caso real: índice sobre columna agregada
    /// por el mismo schema a una BD legacy sin esa columna.
    /// </summary>
    internal static string OmitirIndicesNoEjecutables(string script, SqliteConnection connection)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(
            script,
            @"CREATE\s+(?:UNIQUE\s+)?INDEX\s+IF\s+NOT\s+EXISTS\s+\S+\s+ON\s+(\S+)\s*\(([^)]*)\)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            var tabla = m.Groups[1].Value.Trim().Trim('"', '[', ']', '`');
            var columnas = m.Groups[2].Value
                .Split(',')
                .Select(c => c.Trim().Split(' ')[0].Trim('"', '[', ']', '`'))
                .Where(c => c.Length > 0)
                .ToList();

            var existentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var cmd = connection.CreateCommand())
            {
                // Nombre de tabla ya validado contra el propio script del sistema.
                cmd.CommandText = $"PRAGMA table_info('{tabla.Replace("'", "''")}')";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    existentes.Add(reader.GetString(1));
                }
            }

            if (!TablaExiste(connection, tabla))
            {
                // Tabla nueva: se crea junto con sus índices más adelante en el
                // mismo script — nada que omitir.
                continue;
            }

            if (columnas.All(existentes.Contains))
            {
                continue;
            }

            var faltantes = columnas.Where(c => !existentes.Contains(c)).ToList();
            Log($"Índice omitido — la BD legacy no tiene la(s) columna(s): {string.Join(", ", faltantes)} ({m.Value})");
            script = script.Replace(m.Value, $"/* Índice omitido por DbInitializer: falta(n) {string.Join(", ", faltantes)} */");
        }

        return script;
    }

    /// <summary>
    /// Divide el script en sentencias individuales respetando strings entre
    /// comillas simples y los cuerpos BEGIN...END de los triggers.
    /// </summary>
    /// <summary>
    /// Divide el script en sentencias individuales respetando strings entre
    /// comillas simples y los cuerpos BEGIN...END de los triggers.
    /// </summary>
    internal static IReadOnlyList<string> SplitStatements(string script)
    {
        var statements = new List<string>();
        var current = new StringBuilder();
        var inString = false;
        var enTrigger = false;

        foreach (var rawLine in script.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (!enTrigger && line.TrimStart().StartsWith("CREATE TRIGGER", StringComparison.OrdinalIgnoreCase))
            {
                enTrigger = true;
            }

            foreach (var ch in line)
            {
                if (ch == '\'')
                {
                    inString = !inString;
                }
                current.Append(ch);
            }
            current.Append('\n');

            if (inString)
            {
                continue;
            }

            var acc = current.ToString().Trim();
            if (!acc.EndsWith(";", StringComparison.Ordinal))
            {
                continue;
            }

            // Dentro de un trigger solo el END; final cierra la sentencia
            // completa; los ';' internos (UPDATE, strings) siguen acumulando.
            if (enTrigger && !acc.EndsWith("END;", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var t = acc.Trim();
            statements.Add(t);
            current.Clear();

            if (enTrigger && t.EndsWith("END;", StringComparison.OrdinalIgnoreCase))
            {
                enTrigger = false;
            }
        }

        if (current.ToString().Trim().Length > 0)
        {
            statements.Add(current.ToString().Trim());
        }

        return statements;
    }

    private static void Log(string message) => LogWarning?.Invoke(message);

    private static string ReadSchemaScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(SchemaResource)
            ?? throw new InvalidOperationException($"Recurso embebido no encontrado: {SchemaResource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
