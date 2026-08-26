using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase6;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Promociones;

/// <summary>
/// Venta POS de un combo_membresia: el share del plan sale en VentaInfo para
/// que la UI cree la membresía con VenderAsync (segunda llamada); los productos
/// comparten el resto del precio cerrado con stock descontado. La cancelación
/// de estas ventas se bloquea (venta_mixta_no_cancelable).
/// </summary>
public sealed class ComboMembresiaPosTests
{
    private static async Task<(long idPlan, long idProducto, string idPromo)> CrearComboAsync(
        SecurityTestContext ctx, string token)
    {
        var idPlan = await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = $"Plan-{UuidHelper.NewV4()[..8]}",
            DiasVigencia = 30,
            DiasCongelamientoMax = 0,
            PrecioCentavos = 60000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
        var idProducto = await ctx.Productos.InsertAsync(new Producto
        {
            Descripcion = "Shaker",
            PrecioVentaCentavos = 15000,
            RequiereInventario = true,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });

        var promo = await ctx.PromocionesService.CrearComboMembresiaAsync(
            token, "Membresía + Shaker", null, idPlan, 65000,
            [new PromocionComponente { IdProducto = idProducto, Cantidad = 1 }]);

        return (idPlan, idProducto, promo.Promocion.IdPromocion);
    }

    [Fact]
    public async Task vender_combo_membresia_devuelve_share_del_plan_y_descuenta_stock()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();
        var sedeId = 1L;
        var (idPlan, idProducto, idPromo) = await CrearComboAsync(ctx, token);
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        await Fase6Helper.InsertarInventarioAsync(ctx, idProducto, sedeId, 10);
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdPromocion = idPromo, Cantidad = 1 }],
            IdSocio = idSocio,
            MetodoPago = "efectivo",
        }, sedeId);

        // Precio cerrado 65000; share plan = 65000 * 60000/75000 = 52000.
        Assert.Equal(65000, venta.TotalCentavos);
        Assert.Equal(idPlan, venta.IdPlanComboMembresia);
        Assert.Equal(52000, venta.PlanShareCentavos);

        // Stock del producto descontado.
        Assert.Equal(9, await Fase6Helper.StockAsync(ctx, idProducto, sedeId));

        // En caja entró el precio cerrado completo como un solo movimiento 'venta'.
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var movs = await conn.QueryAsync<(string tipo, long monto, string referenciaTipo)>(
            new CommandDefinition(
                "SELECT tipo, monto_centavos, referencia_tipo FROM caja_movimientos WHERE referencia_id = @id",
                new { id = venta.IdVenta }));
        var mov = Assert.Single(movs);
        Assert.Equal(("ingreso", 65000L, "venta"), (mov.tipo, mov.monto, mov.referenciaTipo));
    }

    [Fact]
    public async Task vender_combo_membresia_crea_membresia_sin_duplicar_caja_ni_cuenta()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();
        var sedeId = 1L;
        var (idPlan, idProducto, idPromo) = await CrearComboAsync(ctx, token);
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        await Fase6Helper.InsertarInventarioAsync(ctx, idProducto, sedeId, 10);
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);

        // Flujo completo de UI (PosPage.ConfirmarCobroConProductosAsync):
        // 1) RegistrarVentaAsync cobra el precio cerrado completo.
        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdPromocion = idPromo, Cantidad = 1 }],
            IdSocio = idSocio,
            MetodoPago = "efectivo",
        }, sedeId);

        // 2) Segunda llamada: crea la membresía con el share, marcada como
        // originada en la venta POS — SIN movimiento de caja ni cuenta.
        var membresia = await ctx.MembresiasService.VenderAsync(
            token,
            idSocio,
            venta.IdPlanComboMembresia!.Value,
            "efectivo",
            venta.PlanShareCentavos,
            sedeId,
            idVentaPosOrigen: venta.IdVenta);

        Assert.Equal(idPlan, membresia.IdPlan);
        Assert.Equal(idSocio, membresia.IdSocio);
        Assert.Equal("activa", membresia.Estado);

        await using var conn = ConnectionFactory.Open(ctx.DbPath);

        // La membresía quedó en la tabla (visible para /membresías).
        var filas = await conn.QuerySingleAsync<long>(
            new CommandDefinition(
                "SELECT COUNT(1) FROM membresias WHERE id_socio = @idSocio AND deleted_at IS NULL",
                new { idSocio }));
        Assert.Equal(1, filas);

        // UN solo ingreso por la venta completa; NINGUNO duplicado por la
        // membresía (referencia pago_membresia no debe existir).
        var movsMembresia = await conn.QuerySingleAsync<long>(
            new CommandDefinition(
                "SELECT COUNT(1) FROM caja_movimientos WHERE referencia_tipo = 'pago_membresia'"));
        Assert.Equal(0, movsMembresia);

        // Sin cuenta fantasma: el precio del combo ya cubre el plan aunque el
        // share prorrateado sea menor a su precio de lista.
        var cuentas = await conn.QuerySingleAsync<long>(
            new CommandDefinition(
                "SELECT COUNT(1) FROM cuentas_cobrar WHERE id_socio = @idSocio",
                new { idSocio }));
        Assert.Equal(0, cuentas);

        // Pago registrado por el share, sin movimiento de caja propio.
        var pagos = await conn.QueryAsync<(long monto, string? idCajaMovimiento)>(
            new CommandDefinition(
                "SELECT monto_centavos, id_caja_movimiento FROM membresias_pagos WHERE id_membresia = @id",
                new { id = membresia.IdMembresia }));
        var pago = Assert.Single(pagos);
        Assert.Equal((venta.PlanShareCentavos, null), (pago.monto, pago.idCajaMovimiento));
    }

    [Fact]
    public async Task cancelar_venta_con_combo_membresia_es_bloqueado()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();
        var sedeId = 1L;
        var (idPlan, idProducto, idPromo) = await CrearComboAsync(ctx, token);
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        await Fase6Helper.InsertarInventarioAsync(ctx, idProducto, sedeId, 10);
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdPromocion = idPromo, Cantidad = 1 }],
            IdSocio = idSocio,
            MetodoPago = "efectivo",
        }, sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.CancelarVentaAsync(token, new CancelarVentaInput
            {
                IdVenta = venta.IdVenta,
                PasswordConfirmacion = Fase4Helper.Password,
            }, sedeId));

        Assert.Equal(BusinessError.Conflict, ex.Error);
        Assert.Equal("venta_mixta_no_cancelable", ex.Code);

        // La venta sigue completada y el stock NO se restaura: la venta quedó
        // registrada (solo se bloqueó la cancelación).
        Assert.Equal(VentaEstados.Completada, await Fase6Helper.EstadoVentaAsync(ctx, venta.IdVenta));
        Assert.Equal(9, await Fase6Helper.StockAsync(ctx, idProducto, sedeId));
    }
}
