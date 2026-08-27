using Microsoft.Data.Sqlite;
using SmartGym.Data.Db;

namespace SmartGym.Tests.Data;

/// <summary>
/// Resiliencia del inicializador ante BD legacy (caso real en producción:
/// índice nuevo sobre columna que la BD existente aún no tiene — crash
/// silencioso 0xc000027B documentado en docs/migracion-dotnet/10).
/// </summary>
public sealed class DbInitializerLegacyTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"smart_gym_legacy_{Guid.NewGuid():N}.db");
    private readonly List<string> _warnings = [];

    public DbInitializerLegacyTests()
    {
        DbInitializer.LogWarning = msg => _warnings.Add(msg);
    }

    public void Dispose()
    {
        DbInitializer.LogWarning = null;
        SqliteConnection.ClearAllPools();
    }

    /// <summary>BD con promociones SIN id_plan (schema legacy) + tabla de planes mínima.</summary>
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

    private bool ExisteIndice(string nombre)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='index' AND name=@n";
        cmd.Parameters.AddWithValue("@n", nombre);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    [Fact]
    public void initialize_sobre_bd_legacy_agrega_columna_y_crea_indice()
    {
        CrearLegacyPromociones();

        // Antes crasheaba (stowed exception WinUI) por el CREATE INDEX
        // sobre la columna inexistente. Ahora la columna se agrega automáticamente.
        var excepcion = Record.Exception(() => DbInitializer.Initialize(_dbPath));
        Assert.Null(excepcion);

        // La columna fue agregada automáticamente (no manualmente).
        Assert.True(ExisteColumna("promociones", "id_plan"));
        Assert.Contains(_warnings, w => w.Contains("id_plan") && w.Contains("agregada"));

        // El índice se creó normalmente (ya no se omite porque la columna existe).
        Assert.True(ExisteIndice("idx_promociones_id_plan"));

        // La fila legacy quedó intacta.
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM promociones WHERE id_promocion = 'promo-legacy'";
        Assert.Equal(1L, (long)(cmd.ExecuteScalar() ?? 0L));

        // El resto del schema SÍ se aplicó.
        cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='planes_membresia'";
        Assert.Equal(1L, (long)(cmd.ExecuteScalar() ?? 0L));
    }

    [Fact]
    public async Task tras_agregar_la_columna_el_indice_se_recupera()
    {
        CrearLegacyPromociones();
        DbInitializer.Initialize(_dbPath);

        // Con auto-columnas, la columna se agrega en el primer Initialize.
        Assert.True(ExisteColumna("promociones", "id_plan"));
        Assert.True(ExisteIndice("idx_promociones_id_plan"));
    }

    [Fact]
    public void split_statements_respeta_triggers_y_strings()
    {
        var script = """
            CREATE TABLE t (a TEXT);
            CREATE TRIGGER trg AFTER INSERT ON t
            BEGIN
                UPDATE t SET a = 'hola; mundo' WHERE a = 'x';
            END;
            CREATE INDEX idx ON t(a);
            """;

        var partes = DbInitializer.SplitStatements(script);

        Assert.Equal(3, partes.Count);
        Assert.Contains("CREATE TABLE", partes[0]);
        Assert.StartsWith("CREATE TRIGGER", partes[1]);
        Assert.EndsWith("END;", partes[1]);
        Assert.StartsWith("CREATE INDEX", partes[2]);
    }

    [Fact]
    public void indice_sobre_columna_nueva_se_crea_por_auto_columnas()
    {
        // BD con promociones legacy SIN id_plan (la tabla SÍ existe): el caso
        // exacto del crash 0xc000027B en producción. Ahora la columna se agrega
        // automáticamente y el índice se crea sin intervención manual.
        CrearLegacyPromociones();

        var ex = Record.Exception(() => DbInitializer.Initialize(_dbPath));
        Assert.Null(ex);
        Assert.True(ExisteColumna("promociones", "id_plan"));
        Assert.True(ExisteIndice("idx_promociones_id_plan"));
    }
}
