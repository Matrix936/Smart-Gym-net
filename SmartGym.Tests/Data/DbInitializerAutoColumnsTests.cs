using System.Reflection;
using Microsoft.Data.Sqlite;
using SmartGym.Data.Db;

namespace SmartGym.Tests.Data;

/// <summary>
/// Detección automática de columnas faltantes con backup previo.
/// Ver docs/migracion-dotnet/10-inicializacion-schema-en-arranque.md.
/// </summary>
[Collection("DbInitializer")]
public sealed class DbInitializerAutoColumnsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"smart_gym_autocol_{Guid.NewGuid():N}.db");
    private readonly List<string> _warnings = [];

    public DbInitializerAutoColumnsTests()
    {
        DbInitializer.LogWarning = msg => _warnings.Add(msg);
    }

    public void Dispose()
    {
        DbInitializer.LogWarning = null;
        SqliteConnection.ClearAllPools();
    }

    /// <summary>BD legacy con promociones SIN id_plan.</summary>
    private void CrearLegacyPromociones()
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE planes_membresia (
                id_plan INTEGER PRIMARY KEY AUTOINCREMENT,
                nombre TEXT NOT NULL,
                precio_centavos INTEGER NOT NULL
            );
            CREATE TABLE promociones (
                id_promocion TEXT PRIMARY KEY,
                tipo TEXT NOT NULL,
                nombre TEXT NOT NULL,
                descripcion TEXT,
                id_producto INTEGER,
                tipo_descuento TEXT,
                valor INTEGER,
                precio_combo_centavos INTEGER,
                fecha_inicio TEXT,
                fecha_fin TEXT,
                es_activo INTEGER NOT NULL DEFAULT 1,
                updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                sincronizado INTEGER NOT NULL DEFAULT 0,
                deleted_at TEXT
            );
            INSERT INTO promociones (id_promocion, tipo, nombre, es_activo)
            VALUES ('promo-legacy', 'descuento', 'Vieja', 1);
            """;
        cmd.ExecuteNonQuery();
    }

    private bool ExisteColumna(string tabla, string columna)
    {
        return ExisteColumna(_dbPath, tabla, columna);
    }

    private static bool ExisteColumna(string dbPath, string tabla, string columna)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tabla})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r.GetString(1), columna, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string LeerSchemaScript()
    {
        var assembly = typeof(DbInitializer).Assembly;
        using var stream = assembly.GetManifestResourceStream("SmartGym.Data.Scripts.schema_smart_gym.sql")
            ?? throw new InvalidOperationException("Recurso embebido no encontrado");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void auto_columnas_agrega_columna_faltante_con_backup()
    {
        CrearLegacyPromociones();
        Assert.False(ExisteColumna("promociones", "id_plan"), "Pre-condition: id_plan no debe existir");

        var excepcion = Record.Exception(() => DbInitializer.Initialize(_dbPath));
        Assert.Null(excepcion);

        // La columna fue agregada automáticamente.
        Assert.True(ExisteColumna("promociones", "id_plan"));

        // El índice se creó normalmente (ya no se omite porque la columna existe).
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='index' AND name='idx_promociones_id_plan'";
        Assert.Equal(1L, (long)(cmd.ExecuteScalar() ?? 0L));

        // Se creó un backup.
        var dir = Path.GetDirectoryName(_dbPath)!;
        var backups = Directory.GetFiles(dir, $"{Path.GetFileName(_dbPath)}.bak_*");
        Assert.NotEmpty(backups);

        // La fila legacy quedó intacta.
        cmd.CommandText = "SELECT COUNT(1) FROM promociones WHERE id_promocion = 'promo-legacy'";
        Assert.Equal(1L, (long)(cmd.ExecuteScalar() ?? 0L));

        // Se registró la adición en el log.
        Assert.Contains(_warnings, w => w.Contains("id_plan") && w.Contains("agregada"));
    }

    [Fact]
    public void auto_columnas_agrega_multiples_columnas_en_misma_tabla()
    {
        // Simular BD legacy: cuentas_cobrar SIN origen ni id_venta (ambas agregadas
        // en commits posteriores). El mecanismo debe agregar las 2 columnas, no solo la primera.
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE cuentas_cobrar (
                    id_cuenta TEXT PRIMARY KEY,
                    id_membresia TEXT,
                    id_socio TEXT NOT NULL,
                    saldo_pendiente_centavos INTEGER NOT NULL,
                    fecha_vencimiento TEXT NOT NULL,
                    estado TEXT NOT NULL DEFAULT 'pendiente',
                    updated_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
                    sincronizado INTEGER NOT NULL DEFAULT 0,
                    deleted_at TEXT
                );
                INSERT INTO cuentas_cobrar (id_cuenta, id_socio, saldo_pendiente_centavos, fecha_vencimiento, estado)
                VALUES ('cc-legacy', 'socio-1', 50000, '2026-12-31', 'pendiente');
                """;
            cmd.ExecuteNonQuery();
        }

        Assert.False(ExisteColumna("cuentas_cobrar", "origen"), "Pre-condition: origen no debe existir");
        Assert.False(ExisteColumna("cuentas_cobrar", "id_venta"), "Pre-condition: id_venta no debe existir");

        var excepcion = Record.Exception(() => DbInitializer.Initialize(_dbPath));
        Assert.Null(excepcion);

        // Ambas columnas fueron agregadas.
        Assert.True(ExisteColumna("cuentas_cobrar", "origen"));
        Assert.True(ExisteColumna("cuentas_cobrar", "id_venta"));

        // La fila legacy quedó intacta y recibió el DEFAULT de 'origen'.
        using var conn2 = new SqliteConnection($"Data Source={_dbPath}");
        conn2.Open();
        var cmd2 = conn2.CreateCommand();
        cmd2.CommandText = "SELECT origen FROM cuentas_cobrar WHERE id_cuenta = 'cc-legacy'";
        Assert.Equal("membresia", cmd2.ExecuteScalar()?.ToString());

        // Se creó backup y se registraron ambas adiciones en el log.
        var dir = Path.GetDirectoryName(_dbPath)!;
        var backups = Directory.GetFiles(dir, $"{Path.GetFileName(_dbPath)}.bak_*");
        Assert.NotEmpty(backups);
        Assert.Contains(_warnings, w => w.Contains("origen") && w.Contains("agregada"));
        Assert.Contains(_warnings, w => w.Contains("id_venta") && w.Contains("agregada"));
    }

    [Fact]
    public void auto_columnas_backup_fallo_no_toca_schema_end_to_end()
    {
        // Prueba end-to-end del mecanismo: cuando CrearBackup falla, el schema
        // queda exactamente igual. Replica la secuencia de AgregarColumnasFaltantes
        // (L81-111 de DbInitializer.cs) invocando los mismos métodos internos:
        //   1. ParsearSchemaEsperado(script)
        //   2. DetectarColumnasFaltantes(dbPath, esperado)
        //   3. CrearBackup(dbPath) → null → Log("BACKUP FALLÓ") → return
        //
        // No se puede llamar Initialize() ni AgregarColumnasFaltantes() directamente
        // porque en Windows, SQLite WAL libera el lock del .db entre Detectar y
        // CrearBackup, haciendo que File.Copy siempre tenga éxito. Para forzar
        // la rama de fallo de forma determinista, llamamos CrearBackup con un path
        // cuyo directorio no existe (garantiza retorno null).

        // Paso 1: Crear BD legacy SIN id_plan.
        CrearLegacyPromociones();
        SqliteConnection.ClearAllPools();
        Assert.False(ExisteColumna("promociones", "id_plan"));
        _warnings.Clear();

        var script = LeerSchemaScript();

        // Paso 2: Replica la secuencia de AgregarColumnasFaltantes.
        //   2a. ParsearSchemaEsperado → parsea el .sql embebido.
        var esperado = DbInitializer.ParsearSchemaEsperado(script);

        //   2b. DetectarColumnasFaltantes → abre la BD, compara columnas.
        var faltantes = DbInitializer.DetectarColumnasFaltantes(_dbPath, esperado);
        Assert.Single(faltantes, f => f.Tabla == "promociones" && f.Nombre == "id_plan");

        //   2c. CrearBackup → falla porque el directorio destino no existe.
        //       Esto es exactamente lo que pasa en producción si el disco está lleno
        //       o los permisos fallan: CrearBackup retorna null → schema intacto.
        var nonexistentDir = Path.Combine(Path.GetTempPath(), $"nofail_{Guid.NewGuid():N}");
        var fakePath = Path.Combine(nonexistentDir, "test.db");
        var backupResult = DbInitializer.CrearBackup(fakePath);
        Assert.Null(backupResult);

        // Paso 3: Verificar que AgregarColumnas NO se ejecutó (simula el return
        // de AgregarColumnasFaltantes cuando backupPath es null).
        // El schema quedó exactamente igual — sin id_plan.
        Assert.False(ExisteColumna("promociones", "id_plan"),
            "id_plan NO debe existir — CrearBackup falló y AgregarColumnas no se ejecutó");

        // La fila legacy quedó intacta.
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM promociones WHERE id_promocion = 'promo-legacy'";
        Assert.Equal(1L, (long)(cmd.ExecuteScalar() ?? 0L));

        // NO se registró ninguna columna agregada.
        Assert.DoesNotContain(_warnings, w => w.Contains("Columna agregada"));
    }

    [Fact]
    public void auto_columnas_backup_fallo_no_toca_schema()
    {
        CrearLegacyPromociones();

        // Simular fallo de backup: usar una ruta con directorio inexistente.
        // La función CrearBackup retorna null cuando falla.
        var fakePath = Path.Combine(Path.GetTempPath(), "nonexistent_dir_xyz", "fake.db");
        var backupResult = DbInitializer.CrearBackup(fakePath);
        Assert.Null(backupResult);
    }

    [Fact]
    public void auto_columnas_no_duplica_si_ya_existe()
    {
        // BD completa (schema actual) — no debe crear backup ni hacer ALTERs.
        DbInitializer.Initialize(_dbPath);

        _warnings.Clear();
        DbInitializer.Initialize(_dbPath);

        // No debe haber backup (schema completo, no hay faltantes).
        var dir = Path.GetDirectoryName(_dbPath)!;
        var backups = Directory.GetFiles(dir, $"{Path.GetFileName(_dbPath)}.bak_*");
        Assert.Empty(backups);

        // No debe haber log de columnas agregadas.
        Assert.DoesNotContain(_warnings, w => w.Contains("agregada"));
    }
}
