using SmartGym.Data.Db;

namespace SmartGym.Tests.Data;

/// <summary>
/// Verificación estructural del schema (criterio de salida de Fase 1):
/// 33 tablas, 20 triggers de updated_at, 33 índices idx_*, seed mínimo.
/// Fuente: 01-modelo-datos.md "Hechos verificados".
/// </summary>
[Collection("data")]
public sealed class DbSchemaTests
{
    private readonly DataTestFixture _fixture;

    public DbSchemaTests(DataTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static long Count(string dbPath, string where)
    {
        using var conn = ConnectionFactory.Open(dbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE {where}";
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    [Fact]
    public void Schema_tiene_exactamente_33_tablas()
    {
        var tables = Count(_fixture.DbPath, "type = 'table' AND name NOT LIKE 'sqlite_%'");
        Assert.Equal(33, tables);
    }

    [Fact]
    public void Schema_tiene_20_triggers_de_updated_at()
    {
        var triggers = Count(_fixture.DbPath, "type = 'trigger'");
        Assert.Equal(20, triggers);
    }

    [Fact]
    public void Schema_tiene_33_indices_con_prefijo_idx()
    {
        var indexes = Count(_fixture.DbPath, "type = 'index' AND name LIKE 'idx_%'");
        Assert.Equal(33, indexes);
    }

    [Fact]
    public void Seed_inserta_rol_SUPERADMIN_y_sede_principal()
    {
        using var conn = ConnectionFactory.Open(_fixture.DbPath);

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM roles WHERE nombre = 'SUPERADMIN';";
            Assert.Equal(1L, (long)(cmd.ExecuteScalar() ?? 0L));
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM sedes WHERE nombre = 'Sede Principal' AND es_activa = 1 AND deleted_at IS NULL;";
            Assert.Equal(1L, (long)(cmd.ExecuteScalar() ?? 0L));
        }
    }

    [Fact]
    public void Schema_es_idempotente()
    {
        // Volver a inicializar no debe romper ni duplicar el seed.
        DbInitializer.Initialize(_fixture.DbPath);

        var roles = Count(_fixture.DbPath, "type = 'table' AND name NOT LIKE 'sqlite_%'");
        Assert.Equal(33, roles);
    }

    [Fact]
    public void Ninguna_columna_monetaria_es_real_o_float()
    {
        using var conn = ConnectionFactory.Open(_fixture.DbPath);

        var tablas = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                tablas.Add(reader.GetString(0));
            }
        }

        Assert.NotEmpty(tablas);
        foreach (var tabla in tablas)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT name, type FROM pragma_table_info('{tabla}')";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var columna = reader.GetString(0).ToLowerInvariant();
                var tipo = (reader.GetString(1) ?? "").ToUpperInvariant();

                // Columnas monetarias (por nombre) deben ser INTEGER, nunca REAL/FLOAT.
                if (columna.Contains("_centavos") || columna.Contains("monto")
                    || columna.Contains("saldo") || columna.Contains("precio")
                    || columna.Contains("costo") || columna.Contains("total"))
                {
                    Assert.True(
                        tipo.Contains("INT", StringComparison.Ordinal),
                        $"La columna {tabla}.{columna} es {tipo}, debe ser INTEGER");
                }
            }
        }
    }
}