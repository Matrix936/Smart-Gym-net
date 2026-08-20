using System.Data;
using Microsoft.Data.Sqlite;
using SmartGym.Data.Db;

namespace SmartGym.Tests.Data;

/// <summary>Criterio de salida Fase 1: PRAGMA foreign_keys = ON + reacción de FK reales.</summary>
[Collection("data")]
public sealed class DbConnectionTests
{
    private readonly DataTestFixture _fixture;

    public DbConnectionTests(DataTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Prgma_foreign_keys_esta_activado_en_cada_conexion()
    {
        using var conn = ConnectionFactory.Open(_fixture.DbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys;";
        Assert.Equal(1L, (long)(cmd.ExecuteScalar() ?? 0L));
    }

    [Fact]
    public async Task Insertar_usuario_con_rol_inexistente_rechaza_por_fk()
    {
        await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            await DbTx.ExecuteAsync(_fixture.DbPath, async (conn, tx) =>
            {
                var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    "INSERT INTO usuarios (nombre, email, password_hash, id_rol) " +
                    "VALUES ('Test', 'test-fk@example.com', 'x', 999999);";
                await cmd.ExecuteNonQueryAsync();
            });
        });
    }

    [Fact]
    public async Task Excepcion_dentro_de_transaccion_hace_rollback()
    {
        await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            await DbTx.ExecuteAsync(_fixture.DbPath, (conn, tx) =>
            {
                // Primera escritura que DEBE deshacerse al fallar la segunda.
                var insert = conn.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText =
                    "INSERT INTO cajas_sesiones (id_sesion, id_usuario, id_sede, monto_inicial_centavos, " +
                    "fecha_apertura, estado) " +
                    "VALUES ('tx-fallback', 999999, (SELECT MIN(id_sede) FROM sedes), 0, '2026-01-01T00:00:00.000Z', 'abierta');";
                insert.ExecuteNonQuery();

                var broken = conn.CreateCommand();
                broken.Transaction = tx;
                broken.CommandText = "INSERT INTO tabla_inexistente (x) VALUES (1);";
                broken.ExecuteNonQuery();
                return Task.CompletedTask;
            });
        });

        // El insert previo debe haberse revertido (usuario 999999 no existe).
        using var conn = ConnectionFactory.Open(_fixture.DbPath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM cajas_sesiones WHERE id_sesion = 'tx-fallback';";
        Assert.Equal(0L, (long)(cmd.ExecuteScalar() ?? 0L));
    }

    [Fact]
    public void Trigger_de_updated_at_se_dispara_al_actualizar()
    {
        using var conn = ConnectionFactory.Open(_fixture.DbPath);

        using var read = conn.CreateCommand();
        read.CommandText = "SELECT updated_at FROM sedes WHERE nombre = 'Sede Principal';";
        var before = (string)read.ExecuteScalar()!;

        // Espera para garantizar un distintivo de milisegundos entre lecturas.
        Thread.Sleep(5);

        using var update = conn.CreateCommand();
        update.CommandText = "UPDATE sedes SET direccion = 'Av. Principal 124' WHERE nombre = 'Sede Principal';";
        update.ExecuteNonQuery();

        using var read2 = conn.CreateCommand();
        read2.CommandText = "SELECT updated_at FROM sedes WHERE nombre = 'Sede Principal';";
        var after = (string)read2.ExecuteScalar()!;

        Assert.False(string.Equals(before, after, StringComparison.Ordinal));
    }
}