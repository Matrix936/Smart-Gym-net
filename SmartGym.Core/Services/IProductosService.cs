using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Services;

public interface IProductosService
{
    Task<PagedResult<Producto>> BuscarAsync(
        string token,
        string? query = null,
        int pagina = 1,
        int tamanoPagina = TamanosPagina.Default,
        bool? esActivo = null,
        CancellationToken ct = default);

    /// <summary>
    /// Alta de producto. Si stockInicial > 0 y requiereInventario, crea la fila
    /// de inventario con ese stock para la sede resuelta.
    /// </summary>
    Task<Producto> CrearAsync(
        string token,
        string descripcion,
        long precioVentaCentavos,
        string? codigoBarras,
        bool requiereInventario,
        long stockInicial,
        long? idSedeFrontend = null,
        CancellationToken ct = default);

    Task<Producto> EditarAsync(
        string token,
        long idProducto,
        string descripcion,
        long precioVentaCentavos,
        string? codigoBarras,
        bool requiereInventario,
        CancellationToken ct = default);

    /// <summary>Soft-desactivación: la fila permanece (ventas históricas intactas) pero no se puede vender.</summary>
    Task DesactivarAsync(string token, long idProducto, CancellationToken ct = default);

    Task ActivarAsync(string token, long idProducto, CancellationToken ct = default);

    /// <summary>Ajuste manual de stock: delta positivo = entrada, negativo = salida.</summary>
    Task<long> AjustarStockAsync(
        string token,
        long idProducto,
        long delta,
        long? idSedeFrontend = null,
        CancellationToken ct = default);
}
