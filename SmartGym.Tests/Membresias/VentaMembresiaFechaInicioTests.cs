using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Membresias;

/// <summary>
/// GetUltimaFechaFinAsync: la fecha_inicio de una nueva membresía no debe
/// heredar de membresías canceladas. Solo debe considerar membresías con
/// estado activa o congelada.
/// </summary>
public sealed class VentaMembresiaFechaInicioTests
{
    [Fact]
    public async Task nueva_venta_no_hereda_fecha_fin_de_membresia_cancelada()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Jesús"), sedeId);
        var planId = await Fase4Helper.CrearPlanAsync(ctx, 1, 0, 5000); // 1 día de vigencia

        // Abrir caja para poder vender
        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);

        // 1ª venta: membresía de 1 día
        var m1 = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, "efectivo", 5000, sedeId);
        Assert.Equal(MembresiaEstados.Activa, m1.Estado);

        // Cancelar la membresía
        await ctx.MembresiasService.CancelarAsync(token, m1.IdMembresia, Fase4Helper.Password);
        var m1Recargada = (await ctx.Membresias.GetByIdAsync(m1.IdMembresia))!;
        Assert.Equal(MembresiaEstados.Cancelada, m1Recargada.Estado);

        // 2ª venta: debería empezar HOY, no heredar de la fecha_fin de la cancelada
        var m2 = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, "efectivo", 5000, sedeId);

        var hoy = DateTime.UtcNow;
        var inicioM2 = DateHelper.ParseIsoUtc(m2.FechaInicio);

        // La fecha de inicio debe ser hoy (o muy cercana), NO la fecha_fin de la membresía cancelada
        Assert.True(inicioM2.Date == hoy.Date,
            $"Esperaba inicio {hoy:yyyy-MM-dd}, pero obtuvo {inicioM2:yyyy-MM-dd} " +
            $"(heredó de la fecha_fin cancelada: {m1.FechaFin})");
    }

    [Fact]
    public async Task nueva_venta_hereda_de_membresia_activa()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Luis"), sedeId);
        var planId = await Fase4Helper.CrearPlanAsync(ctx, 30, 7, 10000); // 30 días

        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);

        // 1ª venta: membresía de 30 días (activa, NO cancelada)
        var m1 = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, "efectivo", 10000, sedeId);
        Assert.Equal(MembresiaEstados.Activa, m1.Estado);

        // 2ª venta: DEBE heredar la fecha_fin de la membresía activa
        var m2 = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, "efectivo", 10000, sedeId);

        var fechaFinM1 = DateHelper.ParseIsoUtc(m1.FechaFin);
        var inicioM2 = DateHelper.ParseIsoUtc(m2.FechaInicio);

        // La fecha de inicio debe ser la fecha_fin de la membresía activa (si es futura)
        // o hoy (si la fecha_fin ya pasó), lo que sea mayor
        var hoy = DateTime.UtcNow;
        var esperado = fechaFinM1 > hoy ? fechaFinM1 : hoy;
        Assert.True(inicioM2.Date == esperado.Date,
            $"Esperaba inicio {esperado:yyyy-MM-dd}, pero obtuvo {inicioM2:yyyy-MM-dd}");
    }

    [Fact]
    public async Task venta_despues_de_cancelada_y_activa_hereda_solo_de_la_activa()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("María"), sedeId);
        var planCorto = await Fase4Helper.CrearPlanAsync(ctx, 1, 0, 5000); // 1 día
        var planLargo = await Fase4Helper.CrearPlanAsync(ctx, 30, 7, 10000); // 30 días

        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);

        // 1ª venta: plan de 1 día → cancelar
        var m1 = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planCorto, "efectivo", 5000, sedeId);
        await ctx.MembresiasService.CancelarAsync(token, m1.IdMembresia, Fase4Helper.Password);

        // 2ª venta: plan de 30 días (activa)
        var m2 = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planLargo, "efectivo", 10000, sedeId);
        Assert.Equal(MembresiaEstados.Activa, m2.Estado);

        // 3ª venta: DEBE heredar de la 2ª (activa), NO de la 1ª (cancelada)
        var m3 = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planCorto, "efectivo", 5000, sedeId);

        var fechaFinM2 = DateHelper.ParseIsoUtc(m2.FechaFin);
        var inicioM3 = DateHelper.ParseIsoUtc(m3.FechaInicio);
        var hoy = DateTime.UtcNow;
        var esperado = fechaFinM2 > hoy ? fechaFinM2 : hoy;

        Assert.True(inicioM3.Date == esperado.Date,
            $"Esperaba inicio {esperado:yyyy-MM-dd}, pero obtuvo {inicioM3:yyyy-MM-dd} " +
            $"(fecha_fin de m2 activa: {m2.FechaFin}, fecha_fin de m1 cancelada: {m1.FechaFin})");
    }
}
