using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase5;
using SmartGym.Tests.Fase6;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Productos;

/// <summary>Catálogo de productos POS: CRUD + ajuste manual de stock.</summary>
public sealed class ProductosTests
{
    [Fact]
    public async Task crear_producto_con_stock_inicial()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();

        var producto = await ctx.ProductosService.CrearAsync(
            token, "Guantes", 25000, codigoBarras: "7501234567890", requiereInventario: true,
            stockInicial: 8, sedeId);

        Assert.True(producto.IdProducto > 0);
        Assert.Equal("Guantes", producto.Descripcion);
        Assert.True(producto.EsActivo);

        var inventario = await ctx.Inventario.GetByProductoSedeAsync(producto.IdProducto, sedeId);
        Assert.NotNull(inventario);
        Assert.Equal(8, inventario!.Stock);
    }

    [Fact]
    public async Task crear_producto_sin_inventario_no_crea_fila_de_stock()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();

        var producto = await ctx.ProductosService.CrearAsync(
            token, "Membresía día", 15000, null, requiereInventario: false, stockInicial: 5, sedeId);

        Assert.Null(await ctx.Inventario.GetByProductoSedeAsync(producto.IdProducto, sedeId));
    }

    [Fact]
    public async Task crear_con_precio_negativo_o_stock_negativo_da_validation()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();

        var exPrecio = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.ProductosService.CrearAsync(token, "X", -1, null, true, 0, sedeId));
        Assert.Equal("precio_invalido", exPrecio.Code);

        var exStock = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.ProductosService.CrearAsync(token, "X", 100, null, true, -3, sedeId));
        Assert.Equal("stock_invalido", exStock.Code);

        var exDescripcion = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.ProductosService.CrearAsync(token, "  ", 100, null, true, 0, sedeId));
        Assert.Equal("descripcion_obligatoria", exDescripcion.Code);
    }

    [Fact]
    public async Task buscar_filtra_por_descripcion_y_codigo_de_barras()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await ctx.ProductosService.CrearAsync(token, "Guantes box", 10000, "750111", true, 0, sedeId);
        await ctx.ProductosService.CrearAsync(token, "Straps", 8000, "750222", true, 0, sedeId);

        var porNombre = await ctx.ProductosService.BuscarAsync(token, "guantes");
        Assert.Equal(1, porNombre.TotalRegistros);
        Assert.Equal("Guantes box", porNombre.Items[0].Descripcion);

        var porCodigo = await ctx.ProductosService.BuscarAsync(token, "750222");
        Assert.Equal(1, porNombre.TotalRegistros);
        Assert.Equal("Straps", porCodigo.Items[0].Descripcion);
    }

    [Fact]
    public async Task editar_modifica_datos_persistidos()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        var producto = await ctx.ProductosService.CrearAsync(token, "Viejo", 100, null, true, 0, sedeId);

        var editado = await ctx.ProductosService.EditarAsync(
            token, producto.IdProducto, "Nuevo nombre", 20000, "750999", requiereInventario: false);

        Assert.Equal("Nuevo nombre", editado.Descripcion);
        Assert.Equal(20000, editado.PrecioVentaCentavos);
        Assert.False(editado.RequiereInventario);
    }

    [Fact]
    public async Task desactivar_impide_venta_pero_la_fila_permanece_para_historial()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        var producto = await ctx.ProductosService.CrearAsync(
            token, "Descontinuado", 5000, null, true, 4, sedeId);

        await ctx.ProductosService.DesactivarAsync(token, producto.IdProducto);

        // Ya no es vendible: GetByIdAsync (el que usa PosService) no lo encuentra.
        Assert.Null(await ctx.Productos.GetByIdAsync(producto.IdProducto));

        // La fila permanece: el detalle de ventas históricas conserva el precio.
        Assert.NotNull(await ctx.Productos.GetByIdCualquierEstadoAsync(producto.IdProducto));

        // Reactivar lo devuelve al catálogo vendible.
        await ctx.ProductosService.ActivarAsync(token, producto.IdProducto);
        Assert.NotNull(await ctx.Productos.GetByIdAsync(producto.IdProducto));
    }

    // ------------------------------------------- escáner POS (cód. de barras)

    [Fact]
    public async Task buscar_por_codigo_barras_es_exacto_y_sin_matching_parcial()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        var producto = await ctx.ProductosService.CrearAsync(
            token, "Guantes", 25000, codigoBarras: "7501234567890", true, 8, sedeId);

        // Coincidencia exacta encuentra.
        var encontrado = await ctx.ProductosService.BuscarPorCodigoBarrasAsync(token, "7501234567890");
        Assert.NotNull(encontrado);
        Assert.Equal(producto.IdProducto, encontrado!.IdProducto);

        // Trim: el escáner puede llegar con espacios accidentales.
        Assert.NotNull(await ctx.ProductosService.BuscarPorCodigoBarrasAsync(token, "  7501234567890  "));

        // Prefijo/subcadena NO matchea (el LIKE parcial de SearchAsync no aplica aquí).
        Assert.Null(await ctx.ProductosService.BuscarPorCodigoBarrasAsync(token, "750123"));
        Assert.Null(await ctx.ProductosService.BuscarPorCodigoBarrasAsync(token, "7501234567891"));

        // Código inexistente y vacío → null (el POS avisa con toast).
        Assert.Null(await ctx.ProductosService.BuscarPorCodigoBarrasAsync(token, "0000000000000"));
        Assert.Null(await ctx.ProductosService.BuscarPorCodigoBarrasAsync(token, "  "));
    }

    [Fact]
    public async Task buscar_por_codigo_barras_ignora_inactivos_y_eliminados()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        var producto = await ctx.ProductosService.CrearAsync(
            token, "Descontinuado", 5000, codigoBarras: "7509876543210", true, 4, sedeId);

        await ctx.ProductosService.DesactivarAsync(token, producto.IdProducto);
        Assert.Null(await ctx.ProductosService.BuscarPorCodigoBarrasAsync(token, "7509876543210"));

        await ctx.ProductosService.ActivarAsync(token, producto.IdProducto);
        Assert.NotNull(await ctx.ProductosService.BuscarPorCodigoBarrasAsync(token, "7509876543210"));
    }

    [Fact]
    public async Task buscar_por_codigo_barras_sin_sesion_falla()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        await ctx.ProductosService.CrearAsync(
            token, "X", 100, codigoBarras: "7501111111111", false, 0, sedeId);

        await Assert.ThrowsAsync<BusinessException>(
            () => ctx.ProductosService.BuscarPorCodigoBarrasAsync("token-invalido", "7501111111111"));
    }

    [Fact]
    public async Task ajuste_stock_entrada_salida_y_stock_insuficiente()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        var producto = await ctx.ProductosService.CrearAsync(
            token, "Proteína", 50000, null, true, 10, sedeId);

        var trasEntrada = await ctx.ProductosService.AjustarStockAsync(
            token, producto.IdProducto, 5, sedeId);
        Assert.Equal(15, trasEntrada);

        var trasSalida = await ctx.ProductosService.AjustarStockAsync(
            token, producto.IdProducto, -7, sedeId);
        Assert.Equal(8, trasSalida);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.ProductosService.AjustarStockAsync(token, producto.IdProducto, -50, sedeId));
        Assert.Equal("stock_insuficiente", ex.Code);

        // Ajuste de entrada en sede sin fila de inventario la crea.
        var otraSede = await Fase4Helper.InsertarSedeAsync(ctx);
        var nuevaFila = await ctx.ProductosService.AjustarStockAsync(
            token, producto.IdProducto, 3, otraSede);
        Assert.Equal(3, nuevaFila);
    }

    [Fact]
    public async Task ajuste_en_producto_sin_inventario_da_validation()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        var producto = await ctx.ProductosService.CrearAsync(
            token, "Servicio", 30000, null, requiereInventario: false, stockInicial: 0, sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.ProductosService.AjustarStockAsync(token, producto.IdProducto, 2, sedeId));
        Assert.Equal("sin_inventario", ex.Code);
    }

    [Fact]
    public async Task gestionar_productos_sin_permiso_falla()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await Fase5Helper.ClearPermisosRolAsync(ctx);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.ProductosService.BuscarAsync(token));
        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.Equal("sin_permiso", ex.Code);
    }
}
