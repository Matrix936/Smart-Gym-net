using Dapper;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class MembresiasCongelamientosRepository : RepositoryBase, IMembresiasCongelamientosRepository
{
    private const string Select = "SELECT id, id_membresia, fecha_inicio, fecha_fin, motivo, autorizado_por, " +
        "updated_at, sincronizado, deleted_at FROM membresias_congelamientos ";

    public MembresiasCongelamientosRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<IReadOnlyList<MembresiaCongelamiento>> GetByMembresiaAsync(string idMembresia, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<MembresiaCongelamiento>(
            new CommandDefinition(
                Select + "WHERE id_membresia = @idMembresia AND deleted_at IS NULL ORDER BY fecha_inicio",
                new { idMembresia }, cancellationToken: ct));
        return rows.ToList();
    }
}