using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase5;
using SmartGym.Tests.Fase6;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Membresias;

/// <summary>Listado paginado de membresias con estado efectivo calculado.</summary>
public sealed class MembresiasBuscarTests
{
    /// <summary>Sembrador: socio con 2 membresias activas (planes distintos).</summary>
    private static async Task<(SecurityTestContext ctx, string token, long sedeId, string idSocio, List<long> planes)> SembrarActivasAsync(int cantidad)
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);

        var planes = new List<long>();
        for (var i = 0; i < cantidad; i++)
        {
            var idPlan = await ctx.Planes.InsertAsync(new PlanMembresia
            {
                Nombre = $"Plan-{i}-{UuidHelper.NewV4()[..8]}",
                DiasVigencia = 30,
                DiasCongelamientoMax = 0,
                PrecioCentavos = 5000,
                EsActivo = true,
                UpdatedAt = DateHelper.NowIsoUtc(),
            });
            await ctx.MembresiasService.VenderAsync(token, idSocio, idPlan, "efectivo", 5000, sedeId);
            planes.Add(idPlan);
        }
        return (ctx, token, sedeId, idSocio, planes);
    }

    [Fact]
    public async Task listar_con_socio_y_plan_resueltos()
    {
        var (ctx, token, sedeId, idSocio, _) = await SembrarActivasAsync(1);

        var pagina = await ctx.MembresiasService.BuscarAsync(token, idSedeFrontend: sedeId);

        Assert.Equal(1, pagina.TotalRegistros);
        var fila = pagina.Items[0];
        Assert.Equal(idSocio, fila.IdSocio);
        Assert.Contains("Juan", fila.NombreSocio);
        Assert.False(string.IsNullOrEmpty(fila.PlanNombre));
        Assert.Equal(MembresiaEstados.Activa, fila.EstadoEfectivo);
    }

    [Fact]
    public async Task filtro_por_estado_efectivo_vencida()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        var idPlanViejo = await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = $"Viejo-{UuidHelper.NewV4()[..8]}",
            DiasVigencia = 7,
            DiasCongelamientoMax = 0,
            PrecioCentavos = 3000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
        // Membresia de 7 dias creada "hace 30 dias" => vencida hoy (se retroacta fecha_fin).
        var membresia = await ctx.MembresiasService.VenderAsync(token, idSocio, idPlanViejo, "efectivo", 3000, sedeId);
        await using (var conn = ConnectionFactory.Open(ctx.DbPath))
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE membresias SET fecha_fin = strftime('%Y-%m-%dT%H:%M:%fZ','now','-23 days') WHERE id_membresia = @id",
                new { id = membresia.IdMembresia }));
        }

        var vencidas = await ctx.MembresiasService.BuscarAsync(
            token, estado: MembresiaEstados.Vencida, idSedeFrontend: sedeId);
        Assert.True(vencidas.TotalRegistros >= 1);
        Assert.All(vencidas.Items, f => Assert.Equal(MembresiaEstados.Vencida, f.EstadoEfectivo));

        // Una activa de verdad no aparece como vencida.
        var idSocio2 = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio2, sedeId);
        var idPlanActivo = await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = $"Vigente-{UuidHelper.NewV4()[..8]}",
            DiasVigencia = 30,
            DiasCongelamientoMax = 0,
            PrecioCentavos = 5000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
        await ctx.MembresiasService.VenderAsync(token, idSocio2, idPlanActivo, "efectivo", 5000, sedeId);

        var activas = await ctx.MembresiasService.BuscarAsync(
            token, estado: MembresiaEstados.Activa, idSedeFrontend: sedeId);
        Assert.All(activas.Items, f => Assert.Equal(MembresiaEstados.Activa, f.EstadoEfectivo));
    }

    [Fact]
    public async Task filtro_por_nombre_de_socio()
    {
        var (ctx, token, sedeId, idSocio, _) = await SembrarActivasAsync(1);
        var otroSocio = UuidHelper.NewV4();
        await ctx.Socios.InsertAsync(new Socio
        {
            IdSocio = otroSocio,
            Nombre = "Ricardo",
            ApellidoPaterno = string.Empty,
            ApellidoMaterno = string.Empty,
            IdSedeRegistro = sedeId,
            Estado = SocioEstados.Activo,
            CreatedAt = DateHelper.NowIsoUtc(),
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
        var idPlan = await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = $"Otro-{UuidHelper.NewV4()[..8]}",
            DiasVigencia = 30,
            DiasCongelamientoMax = 0,
            PrecioCentavos = 5000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
        await ctx.MembresiasService.VenderAsync(token, otroSocio, idPlan, "efectivo", 5000, sedeId);

        var delSocio = await ctx.MembresiasService.BuscarAsync(
            token, nombreSocio: "Juan", idSedeFrontend: sedeId);
        Assert.Equal(1, delSocio.TotalRegistros); // Solo la membresía de ESTE socio (BD aislada por test).
        Assert.All(delSocio.Items, f => Assert.Equal(idSocio, f.IdSocio));
    }

    [Fact]
    public async Task paginacion()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);
        for (var i = 0; i < 3; i++)
        {
            var idSocio = UuidHelper.NewV4();
            await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
            var idPlan = await ctx.Planes.InsertAsync(new PlanMembresia
            {
                Nombre = $"P-{i}-{UuidHelper.NewV4()[..8]}",
                DiasVigencia = 30,
                DiasCongelamientoMax = 0,
                PrecioCentavos = 5000,
                EsActivo = true,
                UpdatedAt = DateHelper.NowIsoUtc(),
            });
            await ctx.MembresiasService.VenderAsync(token, idSocio, idPlan, "efectivo", 5000, sedeId);
        }

        var pagina1 = await ctx.MembresiasService.BuscarAsync(token, pagina: 1, tamanoPagina: 10, idSedeFrontend: sedeId);
        Assert.Equal(3, pagina1.TotalRegistros);
        Assert.Equal(3, pagina1.Items.Count);
        Assert.Equal(1, pagina1.TotalPaginas);
    }

    [Fact]
    public async Task maquinas_de_otra_sede_no_aparecen_en_la_mia()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);
        var otraSede = await Fase4Helper.InsertarSedeAsync(ctx);
        var idSocioOtra = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocioOtra, otraSede);
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, otraSede);
        var idPlan = await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = $"Cross-{UuidHelper.NewV4()[..8]}",
            DiasVigencia = 30,
            DiasCongelamientoMax = 0,
            PrecioCentavos = 5000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
        await ctx.MembresiasService.VenderAsync(token, idSocioOtra, idPlan, "efectivo", 5000, otraSede);

        var mias = await ctx.MembresiasService.BuscarAsync(token, idSedeFrontend: sedeId);
        Assert.Equal(0, mias.TotalRegistros);
    }

    [Fact]
    public async Task listar_sin_permiso_falla()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await Fase5Helper.ClearPermisosRolAsync(ctx);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.MembresiasService.BuscarAsync(token, idSedeFrontend: sedeId));
        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.Equal("sin_permiso", ex.Code);
    }
}
