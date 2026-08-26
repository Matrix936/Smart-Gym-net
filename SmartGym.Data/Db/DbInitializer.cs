using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
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
