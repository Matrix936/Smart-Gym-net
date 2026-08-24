using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

/// <summary>
/// Ventas POS (pos.rs). Vender es atómico: venta + detalle + movimiento de
/// caja + descuento de stock + bitácora. Cancelar restituye stock y registra
/// el egreso correspondiente en la misma transacción.
/// </summary>
public interface IVentasRepository
{
    Task<Venta?> GetByIdAsync(string idVenta, CancellationToken ct = default);

    Task<IReadOnlyList<DetalleVenta>> GetDetallesAsync(string idVenta, CancellationToken ct = default);

    /// <summary>
    /// Inserta venta (estado completada), todos los detalles y el movimiento de caja, y descuenta stock.
    /// cuenta (opcional): cuenta por cobrar de una venta a crédito — se inserta
    /// en la misma transacción (mismo patrón que MembresiasRepository.VenderAsync).
    /// </summary>
    Task InsertarCompletaAsync(
        Venta venta,
        CajaMovimiento movimiento,
        IReadOnlyList<DetalleVenta> detalles,
        IReadOnlyList<(long idProducto, long cantidad)> restarStock,
        BitacoraAuditoria bitacora,
        CuentaCobrar? cuenta = null,
        CancellationToken ct = default);

    /// <summary>
    /// Cancela la venta que aún esté en estado 'completada' (0 filas = ya
    /// cancelada): marca estado, inserta el egreso y restituye stock de los
    /// productos con requerimiento de inventario.
    /// </summary>
    Task CancelarCompletaAsync(
        string idVenta,
        long idSedeVenta,
        CajaMovimiento movimiento,
        BitacoraAuditoria bitacora,
        CuentaCobrar? cuentaParaAnular = null,
        BitacoraAuditoria? bitacoraCuentaAnulada = null,
        CancellationToken ct = default);
}