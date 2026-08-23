using Dapper;
using Microsoft.Data.Sqlite;
using SmartGym.Data.Db;
using SmartGym.Data.Repositories;

namespace SmartGym.Tests.Data;

/// <summary>
/// Regresión del bug de la sede duplicada: el seed corre en CADA arranque
/// (DbInitializer, sin migraciones). Filtraba el NOT EXISTS por nombre
/// 'Sede Principal', así que si el setup renombraba la sede, el segundo
/// arranque re-insertaba una fila duplicada.
/// </summary>
public sealed class SeedSedeTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"smart_gym_seed_{Guid.NewGuid():N}.db");

    [Fact]
    public async Task seed_fresh_inserta_una_sola_sede_principal()
    {
        DbInitializer.Initialize(_dbPath);
        Assert.Equal(1, await TotalSedesActivas());

        var sede = await NewRepo().GetPrincipalAsync();
        Assert.Equal("Sede Principal", sede!.Nombre);
    }

    [Fact]
    public async Task segundo_arranque_tras_rename_no_duplica_la_sede()
    {
        // Arranque 1: seed + setup renombra la sede sembrada.
        DbInitializer.Initialize(_dbPath);
        var repo = NewRepo();
        var sede = await repo.GetPrincipalAsync();
        await repo.RenombrarAsync(sede!.IdSede, "Sucursal Centro");

        // Arranque 2: el inicializador vuelve a correr el script completo.
        DbInitializer.Initialize(_dbPath);

        Assert.Equal(1, await TotalSedesActivas());
        Assert.Equal("Sucursal Centro", (await NewRepo().GetPrincipalAsync())!.Nombre);
    }

    private SedesRepository NewRepo() => new(_dbPath);

    private async Task<int> TotalSedesActivas()
    {
        await using var conn = ConnectionFactory.Open(_dbPath);
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sedes WHERE deleted_at IS NULL");
    }

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // Best-effort cleanup del temporal.
        }
    }
}
