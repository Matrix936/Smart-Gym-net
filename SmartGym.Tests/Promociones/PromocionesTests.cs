using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase5;
using SmartGym.Tests.Fase6;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Promociones;

/// <summary>
/// Módulo Promociones: descuentos por producto y combos. Regla acordada:
/// descuentos solapados sobre el mismo producto se rechazan con error
/// explícito, no se resuelven automáticamente.
/// </summary>
public sealed class PromocionesTests
{
    // ------------------------------------------------------------- catálogo

    [Fact]
    public async Task crear_descuento_porcentaje_queda_activo_y_en_bitacora()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();

        var promo = await ctx.PromocionesService.CrearDescuentoAsync(
            token, "10% proteina", "Promo del mes", idProducto,
            PromocionTiposDescuento.Porcentaje, 10);

        Assert.Equal(PromocionTipos.Descuento, promo.Promocion.Tipo);
        Assert.True(promo.Promocion.EsActivo);
        Assert.True(promo.VigenteHoy);
        Assert.Equal("Proteina 1kg", promo.DescripcionProducto);

        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var accion = await conn.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT accion FROM bitacora_auditoria WHERE accion = 'promocion.creada'"));
        Assert.Equal("promocion.creada", accion);
    }

    [Fact]
    public async Task crear_combo_guarda_componentes_y_subtotal()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var idAgua = await Fase6Helper.InsertarProductoAsync(ctx, "Agua 600ml", 2000);

        var combo = await ctx.PromocionesService.CrearComboAsync(
            token, "Pack entrenamiento", null, 48000,
            [
                new PromocionComponente { IdProducto = idProducto, Cantidad = 1 },
                new PromocionComponente { IdProducto = idAgua, Cantidad = 2 },
            ]);

        Assert.Equal(PromocionTipos.Combo, combo.Promocion.Tipo);
        Assert.Equal(2, combo.Componentes.Count);
        // Subtotal componentes: 50000*1 + 2000*2 = 54000 > precio combo 48000.
        Assert.Equal(54000, combo.SubtotalComponentesCentavos);
    }

    // ------------------------------------------------- validaciones de alta

    [Fact]
    public async Task descuento_solapado_sobre_mismo_producto_es_rechazado()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.PromocionesService.CrearDescuentoAsync(
            token, "10% proteina", null, idProducto, PromocionTiposDescuento.Porcentaje, 10);

        // Sin fechas (extremos abiertos) → siempre solapa con la anterior.
        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PromocionesService.CrearDescuentoAsync(
                token, "Otro descuento", null, idProducto, PromocionTiposDescuento.MontoFijo, 5000));
        Assert.Equal(BusinessError.Conflict, ex.Error);
        Assert.Equal("descuento_solapado", ex.Code);
    }

    [Fact]
    public async Task descuentos_en_productos_distintos_no_solapan()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var idOtro = await Fase6Helper.InsertarProductoAsync(ctx, "Barrita energetica", 15000);
        await ctx.PromocionesService.CrearDescuentoAsync(
            token, "10% proteina", null, idProducto, PromocionTiposDescuento.Porcentaje, 10);

        var otro = await ctx.PromocionesService.CrearDescuentoAsync(
            token, "20% barrita", null, idOtro, PromocionTiposDescuento.Porcentaje, 20);
        Assert.True(otro.VigenteHoy);
    }

    [Fact]
    public async Task porcentaje_mayor_a_100_y_valor_negativo_son_rechazados()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();

        var exPct = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PromocionesService.CrearDescuentoAsync(
                token, "Abusivo", null, idProducto, PromocionTiposDescuento.Porcentaje, 150));
        Assert.Equal("valor_invalido", exPct.Code);

        var exNeg = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PromocionesService.CrearDescuentoAsync(
                token, "Negativo", null, idProducto, PromocionTiposDescuento.MontoFijo, -1));
        Assert.Equal("valor_invalido", exNeg.Code);
    }

    [Fact]
    public async Task combo_sin_componentes_o_con_precio_cero_es_rechazado()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();

        var exSin = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PromocionesService.CrearComboAsync(token, "Combo vacio", null, 10000, []));
        Assert.Equal("combo_sin_componentes", exSin.Code);

        var exPrecio = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PromocionesService.CrearComboAsync(token, "Combo gratis", null, 0,
                [new PromocionComponente { IdProducto = idProducto, Cantidad = 1 }]));
        Assert.Equal("precio_combo_invalido", exPrecio.Code);
    }

    // ------------------------------------------------------------- vigencia

    [Fact]
    public async Task promocion_vencida_no_aparece_como_vigente_hoy()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();

        var vencida = await ctx.PromocionesService.CrearDescuentoAsync(
            token, "Navidad pasado", null, idProducto, PromocionTiposDescuento.Porcentaje, 50,
            fechaFin: DateTime.UtcNow.AddDays(-1));

        Assert.False(vencida.VigenteHoy);

        // Y no llega al POS aunque es_activo siga en true.
        var pos = await ctx.PromocionesService.ObtenerParaPosAsync(token);
        Assert.DoesNotContain(pos, p => p.IdPromocion == vencida.Promocion.IdPromocion);
    }

    // ------------------------------------------------------- kiosco (carrusel)

    [Fact]
    public async Task kiosco_sin_sesion_devuelve_solo_vigentes_y_desactivar_quita()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();

        var descuento = await ctx.PromocionesService.CrearDescuentoAsync(
            token, "10% proteina", null, idProducto, PromocionTiposDescuento.Porcentaje, 10);
        var combo = await ctx.PromocionesService.CrearComboAsync(
            token, "Pack entrenamiento", null, 48000,
            [new PromocionComponente { IdProducto = idProducto, Cantidad = 1 }]);
        var vencida = await ctx.PromocionesService.CrearDescuentoAsync(
            token, "Navidad pasado", null,
            await Fase6Helper.InsertarProductoAsync(ctx, "Barrita energetica", 15000),
            PromocionTiposDescuento.Porcentaje, 50,
            fechaFin: DateTime.UtcNow.AddDays(-1));

        // Sin token: el Kiosco corre sin usuario logueado. Mismo criterio de
        // vigencia efectiva que POS — ambos tipos, nunca vencidas.
        var kiosco = await ctx.PromocionesService.ObtenerVigentesParaKioscoAsync();
        Assert.Contains(kiosco, p => p.IdPromocion == descuento.Promocion.IdPromocion);
        Assert.Contains(kiosco, p => p.IdPromocion == combo.Promocion.IdPromocion);
        Assert.DoesNotContain(kiosco, p => p.IdPromocion == vencida.Promocion.IdPromocion);

        // Desactivar la promo de prueba → desaparece del carrusel sin dejar hueco.
        await ctx.PromocionesService.DesactivarAsync(token, descuento.Promocion.IdPromocion);
        Assert.DoesNotContain(
            await ctx.PromocionesService.ObtenerVigentesParaKioscoAsync(),
            p => p.IdPromocion == descuento.Promocion.IdPromocion);

        // Precios proyectados igual que POS: descuento aplicado y precio cerrado.
        Assert.Equal(45000, kiosco.Single(p => p.IdPromocion == descuento.Promocion.IdPromocion).PrecioFinalCentavos);
        Assert.Equal(48000, kiosco.Single(p => p.IdPromocion == combo.Promocion.IdPromocion).PrecioComboCentavos);
    }

    // --------------------------------------------------- activar/desactivar

    [Fact]
    public async Task desactivar_quita_del_pos_y_activar_regresa()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var combo = await ctx.PromocionesService.CrearComboAsync(
            token, "Pack entrenamiento", null, 48000,
            [new PromocionComponente { IdProducto = idProducto, Cantidad = 1 }]);

        await ctx.PromocionesService.DesactivarAsync(token, combo.Promocion.IdPromocion);
        Assert.DoesNotContain(
            await ctx.PromocionesService.ObtenerParaPosAsync(token),
            p => p.IdPromocion == combo.Promocion.IdPromocion);

        await ctx.PromocionesService.ActivarAsync(token, combo.Promocion.IdPromocion);
        Assert.Contains(
            await ctx.PromocionesService.ObtenerParaPosAsync(token),
            p => p.IdPromocion == combo.Promocion.IdPromocion);

        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var acciones = (await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT accion FROM bitacora_auditoria WHERE accion LIKE 'promocion.%ctivada' ORDER BY created_at"))).ToList();
        Assert.Contains("promocion.desactivada", acciones);
        Assert.Contains("promocion.activada", acciones);
    }

    [Fact]
    public async Task escribir_sin_permiso_es_rechazado()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await Fase5Helper.ClearPermisosRolAsync(ctx);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PromocionesService.CrearDescuentoAsync(
                token, "X", null, idProducto, PromocionTiposDescuento.Porcentaje, 10));
        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.Equal("sin_permiso", ex.Code);
    }

    // ----------------------------------------------------------- venta POS

    [Fact]
    public async Task vender_descuento_aplica_precio_final_y_registra_promo_en_detalle()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);
        await ctx.PromocionesService.CrearDescuentoAsync(
            token, "10% proteina", null, idProducto, PromocionTiposDescuento.Porcentaje, 10);

        // Precio final server-side: 50000 - 10% = 45000.
        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sedeId);

        Assert.Equal(45000, venta.TotalCentavos);

        var stock = await Fase6Helper.StockAsync(ctx, idProducto, sedeId);
        Assert.Equal(9, stock);

        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var promoDetalle = await conn.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT id_promocion FROM detalle_ventas WHERE id_venta = @idVenta",
            new { idVenta = venta.IdVenta }));
        Assert.NotNull(promoDetalle);
    }

    [Fact]
    public async Task vender_combo_descuenta_stock_de_cada_componente()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);
        var idAgua = await Fase6Helper.InsertarProductoAsync(ctx, "Agua 600ml", 2000);
        await Fase6Helper.InsertarInventarioAsync(ctx, idAgua, sedeId, 10);

        var combo = await ctx.PromocionesService.CrearComboAsync(
            token, "Pack entrenamiento", null, 48000,
            [
                new PromocionComponente { IdProducto = idProducto, Cantidad = 1 },
                new PromocionComponente { IdProducto = idAgua, Cantidad = 2 },
            ]);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdPromocion = combo.Promocion.IdPromocion, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sedeId);

        // El precio cerrado manda: 48000, no 54000.
        Assert.Equal(48000, venta.TotalCentavos);
        Assert.Equal(9, await Fase6Helper.StockAsync(ctx, idProducto, sedeId));
        Assert.Equal(8, await Fase6Helper.StockAsync(ctx, idAgua, sedeId));

        // Sum(detalles) == total: prorrateo exacto.
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var sumaDetalles = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT SUM(subtotal_centavos) FROM detalle_ventas WHERE id_venta = @idVenta",
            new { idVenta = venta.IdVenta }));
        Assert.Equal(48000, sumaDetalles);
    }

    [Fact]
    public async Task combo_sin_stock_en_un_componente_rechaza_toda_la_venta()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);
        var idAgua = await Fase6Helper.InsertarProductoAsync(ctx, "Agua 600ml", 2000);
        await Fase6Helper.InsertarInventarioAsync(ctx, idAgua, sedeId, 1);

        var combo = await ctx.PromocionesService.CrearComboAsync(
            token, "Pack entrenamiento", null, 48000,
            [
                new PromocionComponente { IdProducto = idProducto, Cantidad = 1 },
                new PromocionComponente { IdProducto = idAgua, Cantidad = 2 },
            ]);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
            {
                Items = [new VentaItem { IdPromocion = combo.Promocion.IdPromocion, Cantidad = 1 }],
                MetodoPago = "efectivo",
            }, sedeId));
        Assert.Equal(BusinessError.Conflict, ex.Error);
        Assert.Equal("stock_insuficiente", ex.Code);

        // Nada quedó descontado ni vendido.
        Assert.Equal(10, await Fase6Helper.StockAsync(ctx, idProducto, sedeId));
        Assert.Equal(1, await Fase6Helper.StockAsync(ctx, idAgua, sedeId));
    }

    [Fact]
    public async Task combo_con_producto_sin_inventario_no_descuenta_stock()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);
        var idServicio = await Fase6Helper.InsertarProductoAsync(ctx, "Clase personal", 30000, requiereInventario: false);

        var combo = await ctx.PromocionesService.CrearComboAsync(
            token, "Pack con clase", null, 70000,
            [
                new PromocionComponente { IdProducto = idProducto, Cantidad = 1 },
                new PromocionComponente { IdProducto = idServicio, Cantidad = 1 },
            ]);

        await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdPromocion = combo.Promocion.IdPromocion, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sedeId);

        Assert.Equal(9, await Fase6Helper.StockAsync(ctx, idProducto, sedeId));
    }

    [Fact]
    public async Task cancelar_venta_de_combo_restaura_stock_de_componentes()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);
        var idAgua = await Fase6Helper.InsertarProductoAsync(ctx, "Agua 600ml", 2000);
        await Fase6Helper.InsertarInventarioAsync(ctx, idAgua, sedeId, 10);

        var combo = await ctx.PromocionesService.CrearComboAsync(
            token, "Pack entrenamiento", null, 48000,
            [
                new PromocionComponente { IdProducto = idProducto, Cantidad = 1 },
                new PromocionComponente { IdProducto = idAgua, Cantidad = 2 },
            ]);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdPromocion = combo.Promocion.IdPromocion, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sedeId);

        await ctx.PosService.CancelarVentaAsync(token, new CancelarVentaInput
        {
            IdVenta = venta.IdVenta,
            PasswordConfirmacion = Fase4Helper.Password,
        }, sedeId);

        Assert.Equal(10, await Fase6Helper.StockAsync(ctx, idProducto, sedeId));
        Assert.Equal(10, await Fase6Helper.StockAsync(ctx, idAgua, sedeId));
    }
}
