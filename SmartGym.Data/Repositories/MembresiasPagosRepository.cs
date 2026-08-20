using Dapper;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class MembresiasPagosRepository : RepositoryBase, IMembresiasPagosRepository
{
    private const string Select = "SELECT id_pago, id_membresia, monto_centavos, metodo_pago, referencia_pago, " +
        "fecha_pago, id_caja_movimiento, id_vendedor, updated_at, sincronizado, deleted_at " +
        "FROM membresias_pagos ";

    public MembresiasPagosRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<IReadOnlyList<MembresiaPago>> GetByMembresiaAsync(string idMembresia, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<MembresiaPago>(
            new CommandDefinition(
                Select + "WHERE id_membresia = @idMembresia AND deleted_at IS NULL ORDER BY fecha_pago",
                new { idMembresia }, cancellationToken: ct));
        return rows.ToList();
    }
}