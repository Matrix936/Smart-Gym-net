using Dapper;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class PermisosRolRepository : RepositoryBase, IPermisosRolRepository
{
    public PermisosRolRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<IReadOnlyList<PermisoRol>> GetByRolAsync(long idRol, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<PermisoRol>(
            new CommandDefinition(
                "SELECT id, id_rol, accion, created_at FROM permisos_rol WHERE id_rol = @idRol ORDER BY accion",
                new { idRol }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<bool> TieneFilasAsync(CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var count = await conn.ExecuteScalarAsync<long>(
            new CommandDefinition("SELECT COUNT(*) FROM permisos_rol", cancellationToken: ct));
        return count > 0;
    }

    /// <summary>Reemplazo atómico: borra las acciones del rol e inserta la lista nueva.</summary>
    public async Task ReplaceAccionesForRolAsync(long idRol, IEnumerable<string> acciones, CancellationToken ct = default)
    {
        var list = acciones.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
            await conn.ExecuteAsync(
                new CommandDefinition("DELETE FROM permisos_rol WHERE id_rol = @idRol", new { idRol }, tx, cancellationToken: ct));

            foreach (var accion in list)
            {
                await conn.ExecuteAsync(
                    new CommandDefinition(
                        "INSERT INTO permisos_rol (id_rol, accion, created_at) VALUES (@idRol, @accion, @CreatedAt)",
                        new { idRol, accion, CreatedAt = Core.Common.DateHelper.NowIsoUtc() }, tx, cancellationToken: ct));
            }
        }, ct);
    }
}