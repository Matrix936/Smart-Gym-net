using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Tests.Fase4;

namespace SmartGym.Tests.Caja;

/// <summary>Port de cash.rs (12 tests del checklist 03).</summary>
public sealed class CashTests
{
    [Fact]
    public async Task abrir_caja_exitosa_devuelve_sesion_abierta()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        var caja = await ctx.CajaService.AbrirCajaAsync(token, montoInicialCentavos: 5000, sedeId);

        Assert.Equal(CajaEstados.Abierta, caja.Estado);
        Assert.Equal(sedeId, caja.IdSede);
        Assert.Equal(5000, caja.MontoInicialCentavos);
        Assert.Equal(36, caja.IdSesion.Length);
    }

    [Fact]
    public async Task abrir_caja_monto_inicial_negativo_es_rechazado()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CajaService.AbrirCajaAsync(token, -1, sedeId));
        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("monto_negativo", ex.Code);
    }

    [Fact]
    public async Task abrir_caja_doble_en_misma_sede_falla_con_conflict_sin_importar_usuario()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);

        var (localToken, _) = await Fase4Helper.LoginLocalAsync(ctx);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CajaService.AbrirCajaAsync(localToken, 0));
        Assert.Equal(BusinessError.Conflict, ex.Error);
        Assert.Equal("caja_ya_abierta", ex.Code);
    }

    [Fact]
    public async Task abrir_caja_sin_sede_para_sa_sin_param_falla_validacion()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CajaService.AbrirCajaAsync(token, 0));
        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("sede_requerida", ex.Code);
    }

    [Fact]
    public async Task abrir_caja_sede_inactiva_es_rechazada()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();
        var idSedeInactiva = await Fase4Helper.InsertarSedeInactivaAsync(ctx);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CajaService.AbrirCajaAsync(token, 0, idSedeInactiva));
        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("sede_invalida", ex.Code);
    }

    [Fact]
    public async Task abrir_caja_superadmin_con_param_sede_valida_funciona_si_sede_activa()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        var caja = await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);

        Assert.NotNull(caja);
        Assert.Equal(sedeId, caja.IdSede);
    }

    [Fact]
    public async Task cerrar_caja_calcula_monto_esperado_con_movimientos_mixtos()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var caja = await ctx.CajaService.AbrirCajaAsync(token, montoInicialCentavos: 10000, sedeId);

        await Fase4Helper.InsertarMovimientoAsync(ctx, caja.IdSesion, MovimientoTipos.Ingreso, 2000, afectaEfectivo: true);
        await Fase4Helper.InsertarMovimientoAsync(ctx, caja.IdSesion, MovimientoTipos.Egreso, 500, afectaEfectivo: true);
        await Fase4Helper.InsertarMovimientoAsync(ctx, caja.IdSesion, MovimientoTipos.Ingreso, 30000, afectaEfectivo: false);

        var cerrada = await ctx.CajaService.CerrarCajaAsync(token, caja.IdSesion, montoFinalContadoCentavos: 11500);

        Assert.Equal(CajaEstados.Cerrada, cerrada.Estado);
        Assert.Equal(11500, cerrada.MontoEsperadoCentavos);
        Assert.Equal(11500, cerrada.MontoFinalCentavos);
        Assert.NotNull(cerrada.FechaCierre);
    }

    [Fact]
    public async Task cerrar_caja_sin_movimientos_da_esperado_igual_a_inicial()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var caja = await ctx.CajaService.AbrirCajaAsync(token, montoInicialCentavos: 7000, sedeId);

        var cerrada = await ctx.CajaService.CerrarCajaAsync(token, caja.IdSesion, montoFinalContadoCentavos: 7000);

        Assert.Equal(7000, cerrada.MontoEsperadoCentavos);
        Assert.Equal(7000, cerrada.MontoFinalCentavos);
    }

    [Fact]
    public async Task cerrar_caja_inexistente_da_not_found()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CajaService.CerrarCajaAsync(token, "no-existe", 0));
        Assert.Equal(BusinessError.NotFound, ex.Error);
        Assert.Equal("caja_no_encontrada", ex.Code);
    }

    [Fact]
    public async Task cerrar_caja_ya_cerrada_da_conflict()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var caja = await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);
        await ctx.CajaService.CerrarCajaAsync(token, caja.IdSesion, 0);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CajaService.CerrarCajaAsync(token, caja.IdSesion, 0));
        Assert.Equal(BusinessError.Conflict, ex.Error);
        Assert.Equal("caja_ya_cerrada", ex.Code);
    }

    [Fact]
    public async Task cerrar_caja_monto_negativo_es_rechazado()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var caja = await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CajaService.CerrarCajaAsync(token, caja.IdSesion, -1));
        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("monto_negativo", ex.Code);
    }

    [Fact]
    public async Task obtener_caja_abierta_devuelve_some_cuando_h_abierta_y_none_cuando_no()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        var ninguna = await ctx.CajaService.ObtenerCajaAbiertaAsync(token, sedeId);
        Assert.Null(ninguna);

        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);

        var abierta = await ctx.CajaService.ObtenerCajaAbiertaAsync(token, sedeId);
        Assert.NotNull(abierta);
        Assert.Equal(CajaEstados.Abierta, abierta!.Estado);
    }

    [Fact]
    public async Task obtener_caja_abierta_no_encuentra_cerradas()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var caja = await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);
        await ctx.CajaService.CerrarCajaAsync(token, caja.IdSesion, 0);

        var abierta = await ctx.CajaService.ObtenerCajaAbiertaAsync(token, sedeId);
        Assert.Null(abierta);
    }
}