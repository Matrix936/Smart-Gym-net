using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Membresias;

public sealed class PlanesMembresiaRepositoryTests
{
    [Fact]
    public async Task update_edita_nombre_y_precio_de_un_plan_existente()
    {
        using var ctx = new SecurityTestContext();
        var idPlan = await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = "Mensual",
            DiasVigencia = 30,
            DiasCongelamientoMax = 7,
            PrecioCentavos = 10000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });

        var plan = (await ctx.Planes.GetByIdAsync(idPlan))!;
        plan.Nombre = "Mensual Promo";
        plan.PrecioCentavos = 8000;
        plan.UpdatedAt = DateHelper.NowIsoUtc();
        await ctx.Planes.UpdateAsync(plan);

        var actualizado = (await ctx.Planes.GetByIdAsync(idPlan))!;
        Assert.Equal("Mensual Promo", actualizado.Nombre);
        Assert.Equal(8000, actualizado.PrecioCentavos);
        Assert.True(actualizado.EsActivo);
    }

    [Fact]
    public async Task update_plan_inexistente_da_not_found()
    {
        using var ctx = new SecurityTestContext();
        var plan = new PlanMembresia
        {
            IdPlan = 999999,
            Nombre = "No existe",
            DiasVigencia = 30,
            DiasCongelamientoMax = 0,
            PrecioCentavos = 100,
            UpdatedAt = DateHelper.NowIsoUtc(),
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() => ctx.Planes.UpdateAsync(plan));
        Assert.Equal(BusinessError.NotFound, ex.Error);
        Assert.Equal("plan_no_encontrado", ex.Code);
    }

    [Fact]
    public async Task desactivar_un_plan_hace_que_deje_de_aparecer_en_activos()
    {
        using var ctx = new SecurityTestContext();
        var idPlan = await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = "Anual",
            DiasVigencia = 365,
            DiasCongelamientoMax = 15,
            PrecioCentavos = 100000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });

        Assert.Contains(await ctx.Planes.GetActivosAsync(), p => p.IdPlan == idPlan);

        await ctx.Planes.DesactivarAsync(idPlan, DateHelper.NowIsoUtc());

        Assert.DoesNotContain(await ctx.Planes.GetActivosAsync(), p => p.IdPlan == idPlan);
        var plan = (await ctx.Planes.GetByIdAsync(idPlan))!;
        Assert.False(plan.EsActivo);
    }

    [Fact]
    public async Task desactivar_plan_inexistente_da_not_found()
    {
        using var ctx = new SecurityTestContext();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Planes.DesactivarAsync(999999, DateHelper.NowIsoUtc()));
        Assert.Equal(BusinessError.NotFound, ex.Error);
        Assert.Equal("plan_no_encontrado", ex.Code);
    }

    [Fact]
    public async Task activar_un_plan_desactivado_hace_que_vuelva_a_aparecer_en_activos()
    {
        using var ctx = new SecurityTestContext();
        var idPlan = await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = "Trimestral",
            DiasVigencia = 90,
            DiasCongelamientoMax = 5,
            PrecioCentavos = 50000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
        await ctx.Planes.DesactivarAsync(idPlan, DateHelper.NowIsoUtc());
        Assert.DoesNotContain(await ctx.Planes.GetActivosAsync(), p => p.IdPlan == idPlan);

        await ctx.Planes.ActivarAsync(idPlan, DateHelper.NowIsoUtc());

        Assert.Contains(await ctx.Planes.GetActivosAsync(), p => p.IdPlan == idPlan);
        var plan = (await ctx.Planes.GetByIdAsync(idPlan))!;
        Assert.True(plan.EsActivo);
    }

    [Fact]
    public async Task activar_plan_inexistente_da_not_found()
    {
        using var ctx = new SecurityTestContext();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Planes.ActivarAsync(999999, DateHelper.NowIsoUtc()));
        Assert.Equal(BusinessError.NotFound, ex.Error);
        Assert.Equal("plan_no_encontrado", ex.Code);
    }

    [Fact]
    public async Task search_conteo_total_respeta_el_filtro_de_estado()
    {
        using var ctx = new SecurityTestContext();
        for (var i = 1; i <= 3; i++)
        {
            await ctx.Planes.InsertAsync(new PlanMembresia
            {
                Nombre = $"ConEstado{i}",
                DiasVigencia = 30,
                DiasCongelamientoMax = 0,
                PrecioCentavos = 1000,
                EsActivo = true,
                UpdatedAt = DateHelper.NowIsoUtc(),
            });
        }
        await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = "ConEstadoInactivo",
            DiasVigencia = 30,
            DiasCongelamientoMax = 0,
            PrecioCentavos = 1000,
            EsActivo = false,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });

        var activos = await ctx.Planes.SearchAsync(null, pagina: 1, tamanoPagina: TamanosPagina.Diez, esActivo: true);
        var inactivos = await ctx.Planes.SearchAsync(null, pagina: 1, tamanoPagina: TamanosPagina.Diez, esActivo: false);

        Assert.Equal(3, activos.TotalRegistros);
        Assert.All(activos.Items, p => Assert.True(p.EsActivo));
        Assert.Equal(1, inactivos.TotalRegistros);
        Assert.All(inactivos.Items, p => Assert.False(p.EsActivo));
    }
}
