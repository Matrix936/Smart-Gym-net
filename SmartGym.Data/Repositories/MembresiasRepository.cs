using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class MembresiasRepository : RepositoryBase, IMembresiasRepository
{
    private const string Select = "SELECT id_membresia, id_socio, id_plan, id_sede, fecha_inicio, fecha_fin, " +
        "fecha_cancelacion, estado, id_vendedor, updated_at, sincronizado, deleted_at, created_at " +
        "FROM membresias ";

    public MembresiasRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<Membresia?> GetByIdAsync(string idMembresia, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Membresia>(
            new CommandDefinition(
                Select + "WHERE id_membresia = @idMembresia AND deleted_at IS NULL",
                new { idMembresia }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Membresia>> GetBySocioAsync(string idSocio, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<Membresia>(
            new CommandDefinition(
                Select + "WHERE id_socio = @idSocio AND deleted_at IS NULL ORDER BY fecha_fin DESC",
                new { idSocio }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<string?> GetUltimaFechaFinAsync(string idSocio, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.ExecuteScalarAsync<string?>(
            new CommandDefinition(
                "SELECT MAX(fecha_fin) FROM membresias WHERE id_socio = @idSocio AND deleted_at IS NULL",
                new { idSocio }, cancellationToken: ct));
    }

    public async Task VenderAsync(
        Membresia membresia,
        MembresiaPago pago,
        CajaMovimiento movimiento,
        CuentaCobrar? cuenta,
        BitacoraAuditoria bitacora,
        CancellationToken ct = default)
    {
        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
            await conn.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO membresias (id_membresia, id_socio, id_plan, id_sede, fecha_inicio, fecha_fin, " +
                    "estado, id_vendedor, created_at, updated_at, sincronizado) " +
                    "VALUES (@IdMembresia, @IdSocio, @IdPlan, @IdSede, @FechaInicio, @FechaFin, " +
                    "@Estado, @IdVendedor, @CreatedAt, @UpdatedAt, 0);",
                    new
                    {
                        membresia.IdMembresia,
                        membresia.IdSocio,
                        membresia.IdPlan,
                        membresia.IdSede,
                        membresia.FechaInicio,
                        membresia.FechaFin,
                        membresia.Estado,
                        membresia.IdVendedor,
                        membresia.CreatedAt,
                        membresia.UpdatedAt,
                    },
                    tx, cancellationToken: ct));

            await conn.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO caja_movimientos (id_movimiento, id_sesion, tipo, concepto, monto_centavos, " +
                    "metodo_pago, afecta_efectivo, referencia_tipo, referencia_id, created_at, updated_at, sincronizado) " +
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
                    tx, cancellationToken: ct));

            await InsertPagoCoreAsync(conn, tx, pago, ct);

            if (cuenta is not null)
            {
                await conn.ExecuteAsync(
                    new CommandDefinition(
                        "INSERT INTO cuentas_cobrar (id_cuenta, id_membresia, id_socio, saldo_pendiente_centavos, " +
                        "fecha_vencimiento, estado, updated_at, sincronizado) " +
                        "VALUES (@IdCuenta, @IdMembresia, @IdSocio, @SaldoPendienteCentavos, " +
                        "@FechaVencimiento, @Estado, @UpdatedAt, 0);",
                        new
                        {
                            cuenta.IdCuenta,
                            cuenta.IdMembresia,
                            cuenta.IdSocio,
                            cuenta.SaldoPendienteCentavos,
                            cuenta.FechaVencimiento,
                            cuenta.Estado,
                            cuenta.UpdatedAt,
                        },
                        tx, cancellationToken: ct));
            }

            await InsertBitacoraCoreAsync(conn, tx, bitacora, ct);
        }, ct);
    }

    public async Task CongelarAsync(
        MembresiaCongelamiento congelamiento,
        string idMembresia,
        string nuevaFechaFin,
        BitacoraAuditoria bitacora,
        CancellationToken ct = default)
    {
        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
            var affected = await conn.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE membresias SET estado = 'congelada', fecha_fin = @nuevaFechaFin " +
                    "WHERE id_membresia = @idMembresia AND deleted_at IS NULL",
                    new { idMembresia, nuevaFechaFin }, tx, cancellationToken: ct));

            if (affected == 0)
            {
                throw BusinessException.NotFound("membresía no encontrada", "membresia_no_encontrada");
            }

            await conn.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO membresias_congelamientos (id, id_membresia, fecha_inicio, fecha_fin, motivo, " +
                    "autorizado_por, updated_at, sincronizado) " +
                    "VALUES (@Id, @IdMembresia, @FechaInicio, @FechaFin, @Motivo, @AutorizadoPor, @UpdatedAt, 0);",
                    new
                    {
                        congelamiento.Id,
                        congelamiento.IdMembresia,
                        congelamiento.FechaInicio,
                        congelamiento.FechaFin,
                        congelamiento.Motivo,
                        congelamiento.AutorizadoPor,
                        congelamiento.UpdatedAt,
                    },
                    tx, cancellationToken: ct));

            await InsertBitacoraCoreAsync(conn, tx, bitacora, ct);
        }, ct);
    }

    public async Task CancelarAsync(
        string idMembresia,
        string fechaCancelacion,
        BitacoraAuditoria bitacora,
        CancellationToken ct = default)
    {
        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
            var affected = await conn.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE membresias SET estado = 'cancelada', fecha_cancelacion = @fechaCancelacion " +
                    "WHERE id_membresia = @idMembresia AND deleted_at IS NULL",
                    new { idMembresia, fechaCancelacion }, tx, cancellationToken: ct));

            if (affected == 0)
            {
                throw BusinessException.NotFound("membresía no encontrada", "membresia_no_encontrada");
            }

            await InsertBitacoraCoreAsync(conn, tx, bitacora, ct);
        }, ct);
    }

    private static async Task InsertPagoCoreAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx,
        MembresiaPago pago,
        CancellationToken ct)
    {
        await conn.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO membresias_pagos (id_pago, id_membresia, monto_centavos, metodo_pago, referencia_pago, " +
                "fecha_pago, id_caja_movimiento, id_vendedor, updated_at, sincronizado) " +
                "VALUES (@IdPago, @IdMembresia, @MontoCentavos, @MetodoPago, @ReferenciaPago, " +
                "@FechaPago, @IdCajaMovimiento, @IdVendedor, @UpdatedAt, 0);",
                new
                {
                    pago.IdPago,
                    pago.IdMembresia,
                    pago.MontoCentavos,
                    pago.MetodoPago,
                    pago.ReferenciaPago,
                    pago.FechaPago,
                    pago.IdCajaMovimiento,
                    pago.IdVendedor,
                    pago.UpdatedAt,
                },
                tx, cancellationToken: ct));
    }

    private static async Task InsertBitacoraCoreAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx,
        BitacoraAuditoria bitacora,
        CancellationToken ct)
    {
        await conn.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO bitacora_auditoria (id_registro, id_usuario, accion, tabla_afectada, " +
                "id_registro_afectado, valor_anterior, valor_nuevo, id_sede, created_at, updated_at, sincronizado) " +
                "VALUES (@IdRegistro, @IdUsuario, @Accion, @TablaAfectada, " +
                "@IdRegistroAfectado, @ValorAnterior, @ValorNuevo, @IdSede, @CreatedAt, @UpdatedAt, 0);",
                bitacora, tx, cancellationToken: ct));
    }
}