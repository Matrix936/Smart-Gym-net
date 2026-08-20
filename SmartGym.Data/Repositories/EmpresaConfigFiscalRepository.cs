using Dapper;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class EmpresaConfigFiscalRepository : RepositoryBase, IEmpresaConfigFiscalRepository
{
    public EmpresaConfigFiscalRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<EmpresaConfigFiscal?> GetAsync(CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<EmpresaConfigFiscal>(
            new CommandDefinition(
                "SELECT id, nombre_comercial, telefono, direccion, codigo_postal, razon_social, rfc, " +
                "regimen_fiscal, logo_path, updated_at, sincronizado, deleted_at " +
                "FROM empresa_config_fiscal WHERE deleted_at IS NULL ORDER BY id ASC LIMIT 1",
                cancellationToken: ct));
    }

    public async Task SaveAsync(EmpresaConfigFiscal empresa, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var now = Core.Common.DateHelper.NowIsoUtc();
        var existing = await conn.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition("SELECT id FROM empresa_config_fiscal WHERE deleted_at IS NULL ORDER BY id ASC LIMIT 1",
                cancellationToken: ct));

        if (existing is null)
        {
            await conn.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO empresa_config_fiscal (nombre_comercial, telefono, direccion, codigo_postal, " +
                    "razon_social, rfc, regimen_fiscal, logo_path, updated_at, sincronizado) " +
                    "VALUES (@NombreComercial, @Telefono, @Direccion, @CodigoPostal, " +
                    "@RazonSocial, @Rfc, @RegimenFiscal, @LogoPath, @UpdatedAt, @Sincronizado);",
                    new
                    {
                        empresa.NombreComercial,
                        empresa.Telefono,
                        empresa.Direccion,
                        empresa.CodigoPostal,
                        empresa.RazonSocial,
                        empresa.Rfc,
                        empresa.RegimenFiscal,
                        empresa.LogoPath,
                        UpdatedAt = now,
                        empresa.Sincronizado,
                    },
                    cancellationToken: ct));
        }
        else
        {
            await conn.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE empresa_config_fiscal " +
                    "SET nombre_comercial = @NombreComercial, telefono = @Telefono, direccion = @Direccion, " +
                    "codigo_postal = @CodigoPostal, razon_social = @RazonSocial, rfc = @Rfc, " +
                    "regimen_fiscal = @RegimenFiscal, logo_path = @LogoPath, sincronizado = @Sincronizado " +
                    "WHERE id = @Id;",
                    new
                    {
                        empresa.NombreComercial,
                        empresa.Telefono,
                        empresa.Direccion,
                        empresa.CodigoPostal,
                        empresa.RazonSocial,
                        empresa.Rfc,
                        empresa.RegimenFiscal,
                        empresa.LogoPath,
                        empresa.Sincronizado,
                        Id = existing.Value,
                    },
                    cancellationToken: ct));
        }
    }

    public async Task SetLogoPathAsync(string? logoPath, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE empresa_config_fiscal SET logo_path = @logoPath WHERE deleted_at IS NULL",
                new { logoPath }, cancellationToken: ct));
    }
}