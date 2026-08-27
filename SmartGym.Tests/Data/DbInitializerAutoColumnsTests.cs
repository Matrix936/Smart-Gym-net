using Microsoft.Data.Sqlite;
using SmartGym.Data.Db;

namespace SmartGym.Tests.Data;

/// <summary>
/// Detección automática de columnas faltantes con backup previo.
/// Ver docs/migracion-dotnet/10-inicializacion-schema-en-arranque.md.
/// </summary>
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
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
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
