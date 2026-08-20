using Dapper;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class CajaMovimientosRepository : RepositoryBase, ICajaMovimientosRepository
{
    private const string Select = "SELECT id_movimiento, id_sesion, tipo, concepto, monto_centavos, metodo_pago, " +
        "afecta_efectivo, referencia_tipo, referencia_id, created_at, updated_at, sincronizado, deleted_at " +
        "FROM caja_movimientos ";

    public CajaMovimientosRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task InsertAsync(CajaMovimiento movimiento, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO caja_movimientos (id_movimiento, id_sesion, tipo, concepto, monto_centavos, metodo_pago, " +
                "afecta_efectivo, referencia_tipo, referencia_id, created_at, updated_at, sincronizado) " +
                "VALUES (@IdMovimiento, @IdSesion, @Tipo, @Concepto, @MontoCentavos, @MetodoPago, " +
                "@AfectaEfectivo, @ReferenciaTipo, @ReferenciaId, @CreatedAt, @UpdatedAt, 0);",
                new
                {
                    movimiento.IdMovimiento,
                    movimiento.IdSesion,
                    movimiento.Tipo,
                    movimiento.Concepto,
                    movimiento.MontoCentavos,
                    movimiento.MetodoPago,
                    movimiento.AfectaEfectivo,
                    movimiento.ReferenciaTipo,
                    movimiento.ReferenciaId,
                    movimiento.CreatedAt,
                    movimiento.UpdatedAt,
                },
                cancellationToken: ct));
    }

    public async Task<IReadOnlyList<CajaMovimiento>> GetBySesionAsync(string idSesion, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<CajaMovimiento>(
            new CommandDefinition(
                Select + "WHERE id_sesion = @idSesion AND deleted_at IS NULL ORDER BY created_at",
                new { idSesion }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<long> SumarAfectaEfectivoAsync(string idSesion, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.ExecuteScalarAsync<long>(
            new CommandDefinition(
                "SELECT COALESCE(SUM(CASE WHEN tipo = 'ingreso' AND afecta_efectivo = 1 THEN monto_centavos " +
                "WHEN tipo = 'egreso' AND afecta_efectivo = 1 THEN -monto_centavos ELSE 0 END), 0) " +
                "FROM caja_movimientos WHERE id_sesion = @idSesion AND deleted_at IS NULL",
                new { idSesion }, cancellationToken: ct));
    }
}