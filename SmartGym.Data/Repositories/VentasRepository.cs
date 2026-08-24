using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class VentasRepository : RepositoryBase, IVentasRepository
{
    private const string SelectVenta = "SELECT id_venta, id_socio, id_sede, total_centavos, metodo_pago, " +
        "id_caja_movimiento, id_vendedor, estado, updated_at, sincronizado, deleted_at " +
        "FROM ventas ";

    private const string SelectDetalle = "SELECT id_detalle, id_venta, id_producto, cantidad, " +
        "precio_unitario_centavos, subtotal_centavos, updated_at, sincronizado, deleted_at " +
        "FROM detalle_ventas ";

    public VentasRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<Venta?> GetByIdAsync(string idVenta, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Venta>(
            new CommandDefinition(
                SelectVenta + "WHERE id_venta = @idVenta AND deleted_at IS NULL",
                new { idVenta }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<DetalleVenta>> GetDetallesAsync(string idVenta, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<DetalleVenta>(
            new CommandDefinition(
                SelectDetalle + "WHERE id_venta = @idVenta AND deleted_at IS NULL",
                new { idVenta }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task InsertarCompletaAsync(
        Venta venta,
        CajaMovimiento movimiento,
        IReadOnlyList<DetalleVenta> detalles,
        IReadOnlyList<(long idProducto, long cantidad)> restarStock,
        BitacoraAuditoria bitacora,
        CuentaCobrar? cuenta = null,
        CancellationToken ct = default)
    {
        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
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

            await conn.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO ventas (id_venta, id_socio, id_sede, total_centavos, metodo_pago, " +
                    "id_caja_movimiento, id_vendedor, estado, created_at, updated_at, sincronizado) " +
                    "VALUES (@IdVenta, @IdSocio, @IdSede, @TotalCentavos, @MetodoPago, @IdCajaMovimiento, " +
                    "@IdVendedor, @Estado, @CreatedAt, @UpdatedAt, 0);",
                    new
                    {
                        venta.IdVenta,
                        venta.IdSocio,
                        venta.IdSede,
                        venta.TotalCentavos,
                        venta.MetodoPago,
                        venta.IdCajaMovimiento,
                        venta.IdVendedor,
                        venta.Estado,
                        venta.CreatedAt,
                        venta.UpdatedAt,
                    },
                    tx, cancellationToken: ct));

            foreach (var detalle in detalles)
            {
                await conn.ExecuteAsync(
                    new CommandDefinition(
                        "INSERT INTO detalle_ventas (id_detalle, id_venta, id_producto, cantidad, " +
                        "precio_unitario_centavos, subtotal_centavos, updated_at, sincronizado) " +
                        "VALUES (@IdDetalle, @IdVenta, @IdProducto, @Cantidad, @PrecioUnitarioCentavos, " +
                        "@SubtotalCentavos, @UpdatedAt, 0);",
                        detalle, tx, cancellationToken: ct));
            }

            foreach (var (idProducto, cantidad) in restarStock)
            {
                await conn.ExecuteAsync(
                    new CommandDefinition(
                        "UPDATE inventario_sucursal " +
                        "SET stock = stock - @cantidad, updated_at = @ahora " +
                        "WHERE id_producto = @idProducto AND id_sede = @idSede AND deleted_at IS NULL",
                        new { cantidad, ahora = venta.UpdatedAt, idProducto, idSede = venta.IdSede },
                        tx, cancellationToken: ct));
            }

            if (cuenta is not null)
            {
                // Venta a crédito: la cuenta por cobrar vive en la misma transacción.
                await conn.ExecuteAsync(
                    new CommandDefinition(
                        "INSERT INTO cuentas_cobrar (id_cuenta, id_membresia, origen, id_socio, " +
                        "saldo_pendiente_centavos, fecha_vencimiento, estado, updated_at, sincronizado) " +
                        "VALUES (@IdCuenta, @IdMembresia, @Origen, @IdSocio, @SaldoPendienteCentavos, " +
                        "@FechaVencimiento, @Estado, @UpdatedAt, 0);",
                        new
                        {
                            cuenta.IdCuenta,
                            cuenta.IdMembresia,
                            cuenta.Origen,
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

    public async Task CancelarCompletaAsync(
        string idVenta,
        long idSedeVenta,
        CajaMovimiento movimiento,
        BitacoraAuditoria bitacora,
        CancellationToken ct = default)
    {
        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
            var filas = await conn.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE ventas SET estado = 'cancelada', updated_at = @ahora " +
                    "WHERE id_venta = @idVenta AND estado = 'completada' AND deleted_at IS NULL",
                    new { ahora = movimiento.CreatedAt, idVenta }, tx, cancellationToken: ct));

            if (filas == 0)
            {
                throw BusinessException.Conflict("La venta ya esta cancelada", "venta_ya_cancelada");
            }

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

            var itemsRestaurar = await conn.QueryAsync<(long idProducto, long cantidad)>(
                new CommandDefinition(
                    "SELECT dv.id_producto, dv.cantidad " +
                    "FROM detalle_ventas dv " +
                    "JOIN productos p ON p.id_producto = dv.id_producto " +
                    "WHERE dv.id_venta = @idVenta AND p.requiere_inventario = 1 AND dv.deleted_at IS NULL",
                    new { idVenta }, tx, cancellationToken: ct));

            foreach (var (idProducto, cantidad) in itemsRestaurar)
            {
                await conn.ExecuteAsync(
                    new CommandDefinition(
                        "UPDATE inventario_sucursal " +
                        "SET stock = stock + @cantidad, updated_at = @ahora " +
                        "WHERE id_producto = @idProducto AND id_sede = @idSede AND deleted_at IS NULL",
                        new { cantidad, ahora = movimiento.CreatedAt, idProducto, idSede = idSedeVenta },
                        tx, cancellationToken: ct));
            }

            await InsertBitacoraCoreAsync(conn, tx, bitacora, ct);
        }, ct);
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