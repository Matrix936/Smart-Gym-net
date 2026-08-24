using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase5;
using SmartGym.Tests.Fase6;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Cobranza;

/// <summary>Marcar cuentas como incobrables desde Cobranza.</summary>
public sealed class MarcarIncobrableTests
{
    [Fact]
    public async Task marcar_incobrable_cambia_estado()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        var idPlan = await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = $"Plan-{UuidHelper.NewV4()[..8]}",
            DiasVigencia = 30,
            DiasCongelamientoMax = 0,
            PrecioCentavos = 10000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
        await ctx.MembresiasService.VenderAsync(token, idSocio, idPlan, "efectivo", 2000, sedeId);

        var cuenta = (await ctx.CuentasCobrar.BuscarAsync(sedeId, null, null, 1, 10)).Items.First();
        Assert.Equal(CuentaCobrarEstados.Parcial, cuenta.Estado);

        await ctx.CobranzaService.MarcarIncobrableAsync(token, cuenta.IdCuenta);

        var actualizada = await ctx.CuentasCobrar.GetByIdAsync(cuenta.IdCuenta);
        Assert.NotNull(actualizada);
        Assert.Equal(CuentaCobrarEstados.Incobrable, actualizada.Estado);
    }

    [Fact]
    public async Task marcar_incobrable_sobre_cuenta_inexistente_es_rechazado()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CobranzaService.MarcarIncobrableAsync(token, UuidHelper.NewV4()));
        Assert.Equal(BusinessError.NotFound, ex.Error);
    }
}
