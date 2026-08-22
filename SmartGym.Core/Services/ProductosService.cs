using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

/// <summary>
/// Catálogo de productos POS. Mismo patrón que PlanesMembresiaService: la UI
/// nunca escribe directo al repositorio, todo pasa por validación de sesión +
/// permiso. Categorías diferidas: IdCategoria es nullable y el catálogo es útil
/// sin ellas (se agregarán si el negocio lo pide).
/// </summary>
public sealed class ProductosService : IProductosService
{
    private readonly IAuthService _auth;
    private readonly IAuthorizationService _authz;
    private readonly IProductosRepository _productos;
    private readonly IInventarioSucursalRepository _inventario;
    private readonly ISedeResolutionService _sedeResolution;

    public ProductosService(
        IAuthService auth,
        IAuthorizationService authz,
        IProductosRepository productos,
        IInventarioSucursalRepository inventario,
        ISedeResolutionService sedeResolution)
    {
        _auth = auth;
        _authz = authz;
        _productos = productos;
        _inventario = inventario;
        _sedeResolution = sedeResolution;
    }

    public async Task<PagedResult<Producto>> BuscarAsync(
        string token,
        string? query = null,
        int pagina = 1,
        int tamanoPagina = TamanosPagina.Default,
        bool? esActivo = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.ProductosGestionar, ct);

        return await _productos.SearchAsync(query, pagina, tamanoPagina, esActivo, ct);
    }

    public async Task<Producto> CrearAsync(
        string token,
        string descripcion,
        long precioVentaCentavos,
        string? codigoBarras,
        bool requiereInventario,
        long stockInicial,
        long? idSedeFrontend = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.ProductosGestionar, ct);

        ValidarDatos(descripcion, precioVentaCentavos);
        if (stockInicial < 0)
        {
            throw BusinessException.Validation("El stock inicial no puede ser negativo", "stock_invalido");
        }

        var producto = new Producto
        {
            CodigoBarras = NormalizarCodigo(codigoBarras),
            Descripcion = descripcion.Trim(),
            PrecioVentaCentavos = precioVentaCentavos,
            RequiereInventario = requiereInventario,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        };

        producto.IdProducto = await _productos.InsertAsync(producto, ct);

        if (requiereInventario && stockInicial > 0)
        {
            // Stock inicial pertenece a una sede: se resuelve igual que en venta.
            var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);
            await _inventario.InsertAsync(new InventarioSucursal
            {
                IdProducto = producto.IdProducto,
                IdSede = idSede,
                Stock = stockInicial,
                StockMinimo = 0,
                UpdatedAt = DateHelper.NowIsoUtc(),
            }, ct);
        }

        return producto;
    }

    public async Task<Producto> EditarAsync(
        string token,
        long idProducto,
        string descripcion,
        long precioVentaCentavos,
        string? codigoBarras,
        bool requiereInventario,
        CancellationToken ct = default)
    {
        await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.ProductosGestionar, ct);

        ValidarDatos(descripcion, precioVentaCentavos);

        var existente = await ObtenerCualquierEstadoAsync(idProducto, ct);

        existente.Descripcion = descripcion.Trim();
        existente.CodigoBarras = NormalizarCodigo(codigoBarras);
        existente.PrecioVentaCentavos = precioVentaCentavos;
        existente.RequiereInventario = requiereInventario;
        existente.UpdatedAt = DateHelper.NowIsoUtc();

        await _productos.UpdateAsync(existente, ct);
        return existente;
    }

    public async Task DesactivarAsync(string token, long idProducto, CancellationToken ct = default)
    {
        await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.ProductosGestionar, ct);

        _ = await ObtenerCualquierEstadoAsync(idProducto, ct);
        await _productos.DesactivarAsync(idProducto, DateHelper.NowIsoUtc(), ct);
    }

    public async Task ActivarAsync(string token, long idProducto, CancellationToken ct = default)
    {
        await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.ProductosGestionar, ct);

        _ = await ObtenerCualquierEstadoAsync(idProducto, ct);
        await _productos.ActivarAsync(idProducto, DateHelper.NowIsoUtc(), ct);
    }

    public async Task<long> AjustarStockAsync(
        string token,
        long idProducto,
        long delta,
        long? idSedeFrontend = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.ProductosGestionar, ct);

        if (delta == 0)
        {
            throw BusinessException.Validation("El ajuste no puede ser cero", "ajuste_invalido");
        }

        var producto = await ObtenerCualquierEstadoAsync(idProducto, ct);
        if (!producto.RequiereInventario)
        {
            throw BusinessException.Validation("El producto no requiere inventario", "sin_inventario");
        }

        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);
        var ok = await _inventario.AjustarStockAsync(idProducto, idSede, delta, DateHelper.NowIsoUtc(), ct);
        if (!ok)
        {
            // Fila inexistente (nunca tuvo stock en esta sede) solo acepta entradas.
            if (delta > 0 && await _inventario.GetByProductoSedeAsync(idProducto, idSede, ct) is null)
            {
                await _inventario.InsertAsync(new InventarioSucursal
                {
                    IdProducto = idProducto,
                    IdSede = idSede,
                    Stock = delta,
                    StockMinimo = 0,
                    UpdatedAt = DateHelper.NowIsoUtc(),
                }, ct);
                return delta;
            }

            throw BusinessException.Validation(
                $"Stock insuficiente para restar {Math.Abs(delta)} en esta sede", "stock_insuficiente");
        }

        var inventario = (await _inventario.GetByProductoSedeAsync(idProducto, idSede, ct))!.Stock;
        return inventario;
    }

    /// <summary>GetByIdAsync filtra es_activo=1; para editar/desactivar/activar también
    /// necesitamos alcanzar productos inactivos.</summary>
    private async Task<Producto> ObtenerCualquierEstadoAsync(long idProducto, CancellationToken ct)
    {
        return await _productos.GetByIdCualquierEstadoAsync(idProducto, ct)
            ?? throw BusinessException.NotFound("Producto no encontrado", "producto_no_encontrado");
    }

    private static void ValidarDatos(string descripcion, long precioVentaCentavos)
    {
        if (string.IsNullOrWhiteSpace(descripcion))
        {
            throw BusinessException.Validation("La descripción es obligatoria", "descripcion_obligatoria");
        }
        if (precioVentaCentavos < 0)
        {
            throw BusinessException.Validation("El precio no puede ser negativo", "precio_invalido");
        }
    }

    private static string? NormalizarCodigo(string? codigoBarras) =>
        string.IsNullOrWhiteSpace(codigoBarras) ? null : codigoBarras.Trim();
}
