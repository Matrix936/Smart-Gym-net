using Dapper;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class SesionesRepository : RepositoryBase, ISesionesRepository
{
    public SesionesRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task InsertAsync(Sesion sesion, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO sesiones (id_sesion, id_usuario, token_hash, created_at, expires_at, revoked_at) " +
                "VALUES (@IdSesion, @IdUsuario, @TokenHash, @CreatedAt, @ExpiresAt, @RevokedAt);",
                sesion, cancellationToken: ct));
    }

    public async Task<Sesion?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Sesion>(
            new CommandDefinition(
                "SELECT id_sesion, id_usuario, token_hash, created_at, expires_at, revoked_at " +
                "FROM sesiones WHERE token_hash = @tokenHash",
                new { tokenHash }, cancellationToken: ct));
    }

    public async Task RevokeAsync(string tokenHash, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE sesiones SET revoked_at = @revokedAt WHERE token_hash = @tokenHash AND revoked_at IS NULL",
                new { tokenHash, revokedAt = Core.Common.DateHelper.NowIsoUtc() }, cancellationToken: ct));
    }
}