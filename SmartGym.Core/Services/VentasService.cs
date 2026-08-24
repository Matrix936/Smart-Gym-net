using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

public sealed class VentasService : IVentasService
{
    private readonly IAuthService _auth;
    private readonly IAuthorizationService _authz;
    private readonly ICajaMovimientosRepository _movimientos;
    private readonly IVentasRepository _ventas;
    private readonly IProductosRepository _productos;
    private readonly ISedeResolutionService _sedeResolution;
    private readonly IBitacoraAuditoriaRepository _bitacora;

    public VentasService(
        IAuthService auth,
        IAuthorizationService authz,
        ICajaMovimientosRepository movimientos,
        IVentasRepository ventas,
        IProductosRepository productos,
        ISedeResolutionService sedeResolution,
        IBitacoraAuditoriaRepository bitacora)
    {
        _auth = auth;
        _authz = authz;
        _movimientos = movimientos;
        _ventas = ventas;
        _productos = productos;
        _sedeResolution = sedeResolution;
        _bitacora = bitacora;
    }

    public async Task<PagedResult<MovimientoHistorialDto>> BuscarHistorialAsync(
        string token,
        HistorialFiltros? filtros = null,
        int pagina = 1,
        int tamanoPagina = TamanosPagina.Default,
        long? idSedeFrontend = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.PosVerHistorial, ct);
        // Listado: null = todas las sedes (resolver opcional). El detalle y la
        // cancelación siguen exigiendo sede concreta.
        var idSede = await _sedeResolution.ResolverIdSedeOpcionalAsync(info, idSedeFrontend, ct);

        return await _movimientos.BuscarHistorialAsync(idSede, filtros, pagina, tamanoPagina, ct);
    }

    public async Task<VentaInfo> ObtenerDetalleVentaAsync(
        string token,
        string idVenta,
        long? idSedeFrontend = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.PosVerHistorial, ct);
        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);

        var venta = await _ventas.GetByIdAsync(idVenta, ct)
            ?? throw BusinessException.NotFound("Venta no encontrada", "venta_no_encontrada");

        // Mismo código de "no encontrada" para ventas de otra sede: no se filtra
        // su existencia a quien no corresponde verla.
        if (venta.IdSede != idSede)
        {
            throw BusinessException.NotFound("Venta no encontrada", "venta_no_encontrada");
        }

        var detalles = await _ventas.GetDetallesAsync(idVenta, ct);

        var items = new List<DetalleVentaInfo>(detalles.Count);
        foreach (var d in detalles)
        {
            var producto = await _productos.GetByIdAsync(d.IdProducto, ct);
            items.Add(new DetalleVentaInfo
            {
                IdDetalle = d.IdDetalle,
                IdProducto = d.IdProducto,
                Cantidad = d.Cantidad,
                PrecioUnitarioCentavos = d.PrecioUnitarioCentavos,
                SubtotalCentavos = d.SubtotalCentavos,
                DescripcionProducto = producto?.Descripcion,
            });
        }

        var detalle = new VentaInfo
        {
            IdVenta = venta.IdVenta,
            IdSocio = venta.IdSocio,
            IdSede = venta.IdSede,
            TotalCentavos = venta.TotalCentavos,
            MetodoPago = venta.MetodoPago,
            Estado = venta.Estado,
            IdVendedor = venta.IdVendedor,
            Items = items,
        };

        // Quién y cuándo canceló (bitácora) — solo si aplica; el listado ya no
        // muestra la fila de la cancelación como entrada propia.
        if (venta.Estado == VentaEstados.Cancelada)
        {
            if (await _bitacora.ObtenerUltimaCancelacionAsync(idVenta, ct) is { } cancelacion)
            {
                detalle.CanceladaElIsoUtc = cancelacion.FechaIsoUtc;
                detalle.CanceladaPor = cancelacion.Usuario;
            }
        }

        return detalle;
    }
}
