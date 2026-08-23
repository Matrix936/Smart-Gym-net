using Dapper;
using Microsoft.Data.Sqlite;
using SmartGym.Core.Services;
using SmartGym.Data.Db;
using SmartGym.Data.Repositories;
using SmartGym.Data.Storage;

namespace SmartGym.Tests.Data;

/// <summary>
/// Camino del campo opcional DEJADO EN BLANCO: el setup no renombra nada y la
/// sede queda con el fallback del seed ("Sede Principal"). El escenario crítico
/// es el doble arranque (el que reveló el bug original): el seed corre de nuevo
/// y NO debe duplicar la fila aunque su nombre siga siendo exactamente
/// 'Sede Principal' — la condición corregida es por existencia, no por nombre.
/// </summary>
public sealed class SetupSedeFallbackTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"smart_gym_fallback_{Guid.NewGuid():N}.db");
    private readonly string _logosDir =
        Path.Combine(Path.GetTempPath(), $"smart_gym_logos_{Guid.NewGuid():N}");

    [Fact]
    public async Task setup_con_nombre_sede_null_doble_arranque_mantiene_sede_principal_unica()
    {
        await EscenarioDobleArranqueAsync(null);
    }

    [Fact]
    public async Task setup_con_nombre_sede_vacio_doble_arranque_mantiene_sede_principal_unica()
    {
        await EscenarioDobleArranqueAsync("");
    }

    /// <summary>Arranque 1 → setup sin nombre de sede → arranque 2 → una sola fila "Sede Principal".</summary>
    private async Task EscenarioDobleArranqueAsync(string? nombreSede)
    {
        // Arranque 1: schema + seed.
        DbInitializer.Initialize(_dbPath);

        // SetupWizard con el campo de nombre de sede vacío: no renombra nada,
        // la sede queda con el fallback del seed ("Sede Principal").
        var setup = NewSetupService();
        await setup.CompletarConfiguracionInicialAsync(Datos(nombreSede));
        Assert.Equal("Sede Principal", (await NewRepo().GetPrincipalAsync())!.Nombre);

        // Arranque 2: el inicializador vuelve a correr el script completo.
        DbInitializer.Initialize(_dbPath);

        Assert.Equal(1, await TotalSedes());
        Assert.Equal("Sede Principal", (await NewRepo().GetPrincipalAsync())!.Nombre);
    }

    private static SetupDatos Datos(string? nombreSede) => new()
    {
        NombreComercial = "Smart Gym",
        Telefono = "5555555555",
        Direccion = "Av. Principal 123",
        CodigoPostal = "01000",
        Email = $"admin-{Guid.NewGuid():N}@smartgym.test",
        Password = "password123",
        NombreSede = nombreSede,
    };

    private SetupService NewSetupService() => new(
        new UsuariosRepository(_dbPath),
        new RolesRepository(_dbPath),
        new EmpresaConfigFiscalRepository(_dbPath),
        new ConfiguracionRepository(_dbPath),
        new SedesRepository(_dbPath),
        new LogoStorage(_logosDir));

    private SedesRepository NewRepo() => new(_dbPath);

    private async Task<int> TotalSedes()
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
