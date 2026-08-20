using Dapper;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class ConfiguracionRepository : RepositoryBase, IConfiguracionRepository
{
    public ConfiguracionRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<string?> GetAsync(string clave, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(
                "SELECT valor FROM configuracion_general WHERE clave = @clave",
                new { clave }, cancellationToken: ct));
    }

    public async Task SetAsync(string clave, string? valor, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO configuracion_general (clave, valor, updated_at) VALUES (@clave, @valor, @UpdatedAt) " +
                "ON CONFLICT(clave) DO UPDATE SET valor = excluded.valor, updated_at = excluded.updated_at;",
                new { clave, valor, UpdatedAt = Core.Common.DateHelper.NowIsoUtc() }, cancellationToken: ct));
    }
}