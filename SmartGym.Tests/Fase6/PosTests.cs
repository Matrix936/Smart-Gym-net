using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Fase6;

/// <summary>Port de pos.rs (13 tests del checklist 03).</summary>
public sealed class PosTests
{
    [Fact]
    public async Task registrar_venta_exitosa_multi_item()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var idProducto2 = await Fase6Helper.InsertarProductoAsync(ctx, "Creatina", 30000);
        await Fase6Helper.InsertarInventarioAsync(ctx, idProducto2, sedeId, 5);
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items =
            [
                new VentaItem { IdProducto = idProducto, Cantidad = 2 },
                new VentaItem { IdProducto = idProducto2, Cantidad = 1 },
            ],
            IdSocio = null,
            MetodoPago = "efectivo",
        }, sedeId);

        Assert.Equal(Fase6Helper.PrecioProteina * 2 + 30000, venta.TotalCentavos);
        Assert.Equal(2, venta.Items.Count);
        Assert.Equal(VentaEstados.Completada, venta.Estado);
        Assert.Equal(sedeId, venta.IdSede);

        Assert.Equal(8, await Fase6Helper.StockAsync(ctx, idProducto, sedeId));
        Assert.Equal(4, await Fase6Helper.StockAsync(ctx, idProducto2, sedeId));
    }

    [Fact]
    public async Task registrar_venta_con_socio_opcional()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            IdSocio = idSocio,
            MetodoPago = "tarjeta",
        }, sedeId);

        Assert.Equal(idSocio, venta.IdSocio);
        Assert.Equal("tarjeta", venta.MetodoPago);
    }

    [Fact]
    public async Task registrar_venta_sede_inactiva_es_rechazada()
    {
        var (ctx, token, _, idProducto) = await Fase6Helper.BaseAsync();
        var idSedeInactiva = await Fase4Helper.InsertarSedeInactivaAsync(ctx);

        // Antes de unificar ResolverIdSedeAsync, PosService no validaba la
        // sede en absoluto (solo verificaba caja abierta) — este caso no
        // tenía cobertura y el comportamiento viejo lo habría permitido.
        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
            {
                Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
                MetodoPago = "efectivo",
            }, idSedeInactiva));
        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("sede_invalida", ex.Code);
    }

    [Fact]
    public async Task registrar_venta_sin_items_da_validation()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
            {
                Items = [],
                MetodoPago = "efectivo",
            }, sedeId));
        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Contains("item", ex.Message);
    }

    [Fact]
    public async Task registrar_venta_metodo_pago_vacio_da_validation()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
            {
                Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
                MetodoPago = "  ",
            }, sedeId));
        Assert.Equal(BusinessError.Validation, ex.Error);
    }

    [Fact]
    public async Task registrar_venta_stock_insuficiente_da_conflict()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
            {
                Items = [new VentaItem { IdProducto = idProducto, Cantidad = 20 }],
                MetodoPago = "efectivo",
            }, sedeId));
        Assert.Equal(BusinessError.Conflict, ex.Error);
        Assert.Contains("stock", ex.Message);
    }

    [Fact]
    public async Task registrar_venta_producto_inexistente_da_not_found()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
            {
                Items = [new VentaItem { IdProducto = 999999, Cantidad = 1 }],
                MetodoPago = "efectivo",
            }, sedeId));
        Assert.Equal(BusinessError.NotFound, ex.Error);
    }

    [Fact]
    public async Task registrar_venta_sin_caja_abierta_da_conflict()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
            {
                Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
                MetodoPago = "efectivo",
            }, sedeId));
        Assert.Equal(BusinessError.Conflict, ex.Error);
        Assert.Contains("caja", ex.Message);
    }

    [Fact]
    public async Task cancelar_venta_exitosa_restituye_stock()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 3 }],
            MetodoPago = "efectivo",
        }, sedeId);

        Assert.Equal(7, await Fase6Helper.StockAsync(ctx, idProducto, sedeId));

        await ctx.PosService.CancelarVentaAsync(token, new CancelarVentaInput
        {
            IdVenta = venta.IdVenta,
            PasswordConfirmacion = Fase4Helper.Password,
        }, sedeId);

        Assert.Equal(10, await Fase6Helper.StockAsync(ctx, idProducto, sedeId));
        Assert.Equal(VentaEstados.Cancelada, await Fase6Helper.EstadoVentaAsync(ctx, venta.IdVenta));
    }

    [Fact]
    public async Task cancelar_venta_con_clave_incorrecta_falla()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.CancelarVentaAsync(token, new CancelarVentaInput
            {
                IdVenta = venta.IdVenta,
                PasswordConfirmacion = "clave-equivocada",
            }, sedeId));
        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.Contains("Clave", ex.Message);
    }

    [Fact]
    public async Task cancelar_venta_ya_cancelada_da_conflict()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sedeId);

        await ctx.PosService.CancelarVentaAsync(token, new CancelarVentaInput
        {
            IdVenta = venta.IdVenta,
            PasswordConfirmacion = Fase4Helper.Password,
        }, sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.CancelarVentaAsync(token, new CancelarVentaInput
            {
                IdVenta = venta.IdVenta,
                PasswordConfirmacion = Fase4Helper.Password,
            }, sedeId));
        Assert.Equal(BusinessError.Conflict, ex.Error);
    }

    [Fact]
    public async Task cancelar_venta_inexistente_da_not_found()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.CancelarVentaAsync(token, new CancelarVentaInput
            {
                IdVenta = "no-existe",
                PasswordConfirmacion = Fase4Helper.Password,
            }, sedeId));
        Assert.Equal(BusinessError.NotFound, ex.Error);
    }

    [Fact]
    public async Task cancelar_venta_sin_caja_abierta_da_conflict()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var caja = await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sedeId);

        await ctx.CajaService.CerrarCajaAsync(token, caja.IdSesion, 150000);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.CancelarVentaAsync(token, new CancelarVentaInput
            {
                IdVenta = venta.IdVenta,
                PasswordConfirmacion = Fase4Helper.Password,
            }, sedeId));
        Assert.Equal(BusinessError.Conflict, ex.Error);
        Assert.Contains("caja", ex.Message);
    }

    [Fact]
    public async Task cancelar_venta_calcula_monto_esperado_correctamente()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var caja = await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sedeId);
        Assert.Equal(Fase6Helper.PrecioProteina, venta.TotalCentavos);

        await ctx.PosService.CancelarVentaAsync(token, new CancelarVentaInput
        {
            IdVenta = venta.IdVenta,
            PasswordConfirmacion = Fase4Helper.Password,
        }, sedeId);

        var montoEsperadoMovimientos = await ctx.Movimientos.SumarAfectaEfectivoAsync(caja.IdSesion);
        Assert.Equal(0, montoEsperadoMovimientos);

        var montoEsperadoTotal = 100000 + montoEsperadoMovimientos;
        Assert.Equal(100000, montoEsperadoTotal);
    }
}