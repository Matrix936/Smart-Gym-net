using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Membresias;

/// <summary>Port de memberships.rs (14 tests del checklist 03).</summary>
public sealed class MembershipsTests
{
    private const int DiasVigencia = 30;
    private const int CongelamientoMax = 10;
    private const long Precio = 10000;

    /// <summary>Superadmin listo + plan dado de alta (precio 10000, 30 días, 10 de congelamiento máx).</summary>
    private static async Task<(SecurityTestContext ctx, string token, long sedeId, long planId)> EscenarioAsync()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var planId = await Fase4Helper.CrearPlanAsync(ctx, DiasVigencia, CongelamientoMax, Precio);
        return (ctx, token, sedeId, planId);
    }

    [Fact]
    public async Task vender_membresia_exitosa_crea_membresia_pago_y_movimiento_caja()
    {
        var (ctx, token, sedeId, planId) = await EscenarioAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Ana"), sedeId);
        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);

        var m = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, Fase4Helper.MetodoPago, Precio, sedeId);

        Assert.Equal(sedeId, m.IdSede);
        Assert.Equal(MembresiaEstados.Activa, m.Estado);
        Assert.Equal(DiasVigencia, (DateHelper.ParseIsoUtc(m.FechaFin) - DateHelper.ParseIsoUtc(m.FechaInicio)).Days);

        var pago = Assert.Single(await ctx.Pagos.GetByMembresiaAsync(m.IdMembresia));
        Assert.Equal(Precio, pago.MontoCentavos);

        var caja = (await ctx.CajaService.ObtenerCajaAbiertaAsync(token, sedeId))!;
        var movimiento = Assert.Single(await ctx.Movimientos.GetBySesionAsync(caja.IdSesion));
        Assert.Equal(MovimientoTipos.Ingreso, movimiento.Tipo);
        Assert.Equal(CajaReferenciaTipos.PagoMembresia, movimiento.ReferenciaTipo);
        Assert.Equal(pago.IdPago, movimiento.ReferenciaId);
        Assert.Equal(Precio, movimiento.MontoCentavos);

        Assert.Null(await ctx.CuentasCobrar.GetByMembresiaAsync(m.IdMembresia));
    }

    [Fact]
    public async Task vender_membresia_con_monto_menor_genera_cuenta_cobrar_con_saldo()
    {
        var (ctx, token, sedeId, planId) = await EscenarioAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Bruno"), sedeId);
        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);

        var m = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, Fase4Helper.MetodoPago, 4000, sedeId);

        var cuenta = await ctx.CuentasCobrar.GetByMembresiaAsync(m.IdMembresia);
        Assert.NotNull(cuenta);
        Assert.Equal(6000, cuenta!.SaldoPendienteCentavos);
        Assert.Equal(CuentaCobrarEstados.Parcial, cuenta.Estado);

        var pago = Assert.Single(await ctx.Pagos.GetByMembresiaAsync(m.IdMembresia));
        Assert.Equal(4000, pago.MontoCentavos);
    }

    [Fact]
    public async Task vender_membresia_sin_caja_abierta_da_error_claro()
    {
        var (ctx, token, sedeId, planId) = await EscenarioAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Carla"), sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, Fase4Helper.MetodoPago, Precio, sedeId));
        Assert.Equal(BusinessError.Conflict, ex.Error);
        Assert.Equal("caja_no_abierta", ex.Code);
    }

    [Fact]
    public async Task vender_membresia_monto_negativo_o_excesivo_es_rechazado()
    {
        var (ctx, token, sedeId, planId) = await EscenarioAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("David"), sedeId);
        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);

        var negativo = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, Fase4Helper.MetodoPago, -1, sedeId));
        Assert.Equal(BusinessError.Validation, negativo.Error);
        Assert.Equal("monto_invalido", negativo.Code);

        var excesivo = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, Fase4Helper.MetodoPago, Precio + 1, sedeId));
        Assert.Equal(BusinessError.Validation, excesivo.Error);
        Assert.Equal("monto_excesivo", excesivo.Code);
    }

    [Fact]
    public async Task vender_membresia_plan_inexistente_da_not_found()
    {
        var (ctx, token, sedeId, _) = await EscenarioAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Elena"), sedeId);
        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.MembresiasService.VenderAsync(token, socio.IdSocio, 999999, Fase4Helper.MetodoPago, Precio, sedeId));
        Assert.Equal(BusinessError.NotFound, ex.Error);
        Assert.Equal("plan_no_encontrado", ex.Code);
    }

    [Fact]
    public async Task vender_membresia_socio_inexistente_da_not_found()
    {
        var (ctx, token, sedeId, planId) = await EscenarioAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.MembresiasService.VenderAsync(token, UuidHelper.NewV4(), planId, Fase4Helper.MetodoPago, Precio, sedeId));
        Assert.Equal(BusinessError.NotFound, ex.Error);
        Assert.Equal("socio_no_encontrado", ex.Code);
    }

    [Fact]
    public async Task renovacion_reusa_fecha_fin_anterior_no_pierde_dias()
    {
        var (ctx, token, sedeId, planId) = await EscenarioAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Fernando"), sedeId);
        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);

        var primera = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, Fase4Helper.MetodoPago, Precio, sedeId);

        var renovacion = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, Fase4Helper.MetodoPago, Precio, sedeId);

        Assert.Equal(primera.FechaFin, renovacion.FechaInicio);
        Assert.Equal(DiasVigencia, (DateHelper.ParseIsoUtc(renovacion.FechaFin) - DateHelper.ParseIsoUtc(renovacion.FechaInicio)).Days);
    }

    [Fact]
    public async Task congelar_membresia_respeta_dias_max_y_extiende_fecha_fin()
    {
        var (ctx, token, sedeId, planId) = await EscenarioAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Gabriela"), sedeId);
        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);
        var m = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, Fase4Helper.MetodoPago, Precio, sedeId);

        var desde = DateHelper.ToIsoUtc(DateTime.UtcNow.AddDays(10));
        var hasta = DateHelper.ToIsoUtc(DateTime.UtcNow.AddDays(15));

        var congelada = await ctx.MembresiasService.CongelarAsync(token, m.IdMembresia, desde, hasta, "Vacaciones");

        Assert.Equal(MembresiaEstados.Congelada, congelada.Estado);
        Assert.Equal(DateHelper.ToIsoUtc(DateHelper.ParseIsoUtc(m.FechaFin).AddDays(5)), congelada.FechaFin);

        var registro = Assert.Single(await ctx.Congelamientos.GetByMembresiaAsync(m.IdMembresia));
        Assert.Equal(desde, registro.FechaInicio);
        Assert.Equal(hasta, registro.FechaFin);
    }

    [Fact]
    public async Task congelar_membresia_excede_dias_max_da_error()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var planId = await Fase4Helper.CrearPlanAsync(ctx, DiasVigencia, diasCongelamientoMax: 3, Precio);
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Hugo"), sedeId);
        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);
        var m = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, Fase4Helper.MetodoPago, Precio, sedeId);

        var desde = DateHelper.ToIsoUtc(DateTime.UtcNow.AddDays(10));
        var hasta = DateHelper.ToIsoUtc(DateTime.UtcNow.AddDays(16));

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.MembresiasService.CongelarAsync(token, m.IdMembresia, desde, hasta));
        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("dias_congelamiento_excedido", ex.Code);
    }

    [Fact]
    public async Task congelar_membresia_inexistente_da_not_found()
    {
        var (ctx, token, _, _) = await EscenarioAsync();
        var desde = DateHelper.ToIsoUtc(DateTime.UtcNow.AddDays(10));
        var hasta = DateHelper.ToIsoUtc(DateTime.UtcNow.AddDays(15));

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.MembresiasService.CongelarAsync(token, UuidHelper.NewV4(), desde, hasta));
        Assert.Equal(BusinessError.NotFound, ex.Error);
        Assert.Equal("membresia_no_encontrada", ex.Code);
    }

    [Fact]
    public async Task cancelar_membresia_con_clave_correcta_funciona()
    {
        var (ctx, token, sedeId, planId) = await EscenarioAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Iris"), sedeId);
        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);
        var m = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, Fase4Helper.MetodoPago, Precio, sedeId);

        await ctx.MembresiasService.CancelarAsync(token, m.IdMembresia, Fase4Helper.Password);

        var actual = (await ctx.Membresias.GetByIdAsync(m.IdMembresia))!;
        Assert.Equal(MembresiaEstados.Cancelada, actual.Estado);
        Assert.NotNull(actual.FechaCancelacion);
    }

    [Fact]
    public async Task cancelar_membresia_con_clave_incorrecta_falla()
    {
        var (ctx, token, sedeId, planId) = await EscenarioAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Javier"), sedeId);
        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);
        var m = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, Fase4Helper.MetodoPago, Precio, sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.MembresiasService.CancelarAsync(token, m.IdMembresia, "clave-mala"));
        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.Equal("clave_incorrecta", ex.Code);

        var actual = (await ctx.Membresias.GetByIdAsync(m.IdMembresia))!;
        Assert.Equal(MembresiaEstados.Activa, actual.Estado);
    }

    [Fact]
    public async Task cancelar_membresia_inexistente_da_not_found()
    {
        var (ctx, token, _, _) = await EscenarioAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.MembresiasService.CancelarAsync(token, UuidHelper.NewV4(), Fase4Helper.Password));
        Assert.Equal(BusinessError.NotFound, ex.Error);
        Assert.Equal("membresia_no_encontrada", ex.Code);
    }

    [Fact]
    public async Task cancelar_membresia_ya_cancelada_da_conflict()
    {
        var (ctx, token, sedeId, planId) = await EscenarioAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Karla"), sedeId);
        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);
        var m = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, Fase4Helper.MetodoPago, Precio, sedeId);

        await ctx.MembresiasService.CancelarAsync(token, m.IdMembresia, Fase4Helper.Password);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.MembresiasService.CancelarAsync(token, m.IdMembresia, Fase4Helper.Password));
        Assert.Equal(BusinessError.Conflict, ex.Error);
        Assert.Equal("membresia_ya_cancelada", ex.Code);
    }
}