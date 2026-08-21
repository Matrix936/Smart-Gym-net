using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Services;

public interface IVentasService
{
    /// <summary>
    /// Historial unificado de la sede: ventas POS, cancelaciones, pagos de
    /// membresía y abonos (todo lo que movió dinero en caja).
    /// </summary>
    Task<PagedResult<MovimientoHistorialDto>> BuscarHistorialAsync(
        string token,
        HistorialFiltros? filtros = null,
        int pagina = 1,
        int tamanoPagina = TamanosPagina.Default,
        long? idSedeFrontend = null,
        CancellationToken ct = default);

    /// <summary>Detalle de una venta POS (items con descripción de producto).</summary>
    Task<VentaInfo> ObtenerDetalleVentaAsync(
        string token,
        string idVenta,
        long? idSedeFrontend = null,
        CancellationToken ct = default);
}
