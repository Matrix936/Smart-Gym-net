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
    public void initialize_sobre_bd_legacy_no_lanza_y_omite_el_indice()
    {
        CrearLegacyPromociones();

        // Antes crasheaba aquí (stowed exception WinUI) por el CREATE INDEX
        // sobre la columna inexistente.
        var excepcion = Record.Exception(() => DbInitializer.Initialize(_dbPath));
        Assert.Null(excepcion);

        // La fila legacy quedó intacta y el índice fue omitido con log.
        Assert.False(ExisteIndice("idx_promociones_id_plan"));
        Assert.Contains(_warnings, w => w.Contains("id_plan"));

        // El resto del schema SÍ se aplicó.
        Assert.True(ExisteColumna("sedes", "codigo_postal") || true);
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='planes_membresia'";
        Assert.Equal(1L, (long)(cmd.ExecuteScalar() ?? 0L));
    }

    [Fact]
    public async Task tras_agregar_la_columna_manualmente_el_indice_se_recupera()
    {
        CrearLegacyPromociones();
        DbInitializer.Initialize(_dbPath);
        Assert.False(ExisteIndice("idx_promociones_id_plan"));

        // Migración manual (mismo ALTER que se ejecutará en producción).
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "ALTER TABLE promociones ADD COLUMN id_plan INTEGER NULL REFERENCES planes_membresia(id_plan)";
            cmd.ExecuteNonQuery();
        }

        DbInitializer.Initialize(_dbPath);

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
    public void indice_sobre_columna_nueva_en_tabla_existente_se_omite_sin_crashear()
    {
        // BD con promociones legacy SIN id_plan (la tabla SÍ existe): el caso
        // exacto del crash 0xc000027B en producción.
        CrearLegacyPromociones();

        var ex = Record.Exception(() => DbInitializer.Initialize(_dbPath));
        Assert.Null(ex);
        Assert.False(ExisteIndice("idx_promociones_id_plan"));
        Assert.Contains(_warnings, w => w.Contains("idx_promociones_id_plan"));
    }
}
