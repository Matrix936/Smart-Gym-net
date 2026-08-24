using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase5;
using SmartGym.Tests.Fase6;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Accesos;

/// <summary>Historial de accesos: listado paginado con filtros sobre accesos_bitacora.</summary>
public sealed class HistorialAccesoTests
{
    /// <summary>Sembrador: socio activo con membresía vigente (el Kiosco le concede).</summary>
    private static async Task RetrocederAsync(SecurityTestContext ctx, string idAcceso)
    {
        await ModoRegistroAccesoTests.RetrocederTimestampAsync(ctx, idAcceso, minutos: 5);
    }
    private static async Task<(SecurityTestContext ctx, string token, long sedeId, string idSocio)> SembrarConcedidoAsync()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        var idPlan = await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = $"Plan-{UuidHelper.NewV4()[..8]}",
            DiasVigencia = 30,
            DiasCongelamientoMax = 0,
            PrecioCentavos = 5000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
        await ctx.MembresiasService.VenderAsync(token, idSocio, idPlan, "efectivo", 5000, sedeId);
        return (ctx, token, sedeId, idSocio);
    }

    [Fact]
    public async Task concedido_y_denegado_se_listan_con_motivo()
    {
        var (ctx, token, sedeId, idSocio) = await SembrarConcedidoAsync();

        // Concedido por huella (Kiosco).
        var concedido = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId);
        Assert.Equal(AccesoEstados.Concedido, concedido.Estado);

        // Denegado manual: socio INACTIVO existente => fila denegada con motivo.
        var idInactivo = UuidHelper.NewV4();
        await ctx.Socios.InsertAsync(new Socio
        {
            IdSocio = idInactivo,
            Nombre = "Bloqueado",
            ApellidoPaterno = string.Empty,
            ApellidoMaterno = string.Empty,
            IdSedeRegistro = sedeId,
            Estado = SocioEstados.Inactivo,
            CreatedAt = DateHelper.NowIsoUtc(),
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
        var denegado = await ctx.AccesoService.RegistrarAccesoManualAsync(token, idInactivo, sedeId);
        Assert.Equal(AccesoEstados.Denegado, denegado.Estado);

        var pagina = await ctx.AccesoService.BuscarAsync(token, idSedeFrontend: sedeId);

        Assert.Equal(2, pagina.TotalRegistros);
        Assert.Contains(pagina.Items, f => f.Estado == AccesoEstados.Concedido && f.Metodo == AccesoMetodos.Huella);
        var filaDenegada = Assert.Single(pagina.Items, f => f.Estado == AccesoEstados.Denegado);
        Assert.False(string.IsNullOrEmpty(filaDenegada.MotivoDenegacion));
    }

    [Fact]
    public async Task filtro_por_estado()
    {
        var (ctx, token, sedeId, idSocio) = await SembrarConcedidoAsync();
        var rKiosco = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId);

        // Denegado: socio bloqueado no existe aqui; usamos socio sin membresía via manual.
        var otroSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, otroSocio, sedeId);
        await ctx.AccesoService.RegistrarAccesoManualAsync(token, otroSocio, sedeId);

        await RetrocederAsync(ctx, rKiosco.IdAcceso);

        var concedidos = await ctx.AccesoService.BuscarAsync(
            token, new AccesoHistorialFiltros { Estado = AccesoEstados.Concedido }, idSedeFrontend: sedeId);
        Assert.All(concedidos.Items, f => Assert.Equal(AccesoEstados.Concedido, f.Estado));

        var denegados = await ctx.AccesoService.BuscarAsync(
            token, new AccesoHistorialFiltros { Estado = AccesoEstados.Denegado }, idSedeFrontend: sedeId);
        Assert.All(denegados.Items, f => Assert.Equal(AccesoEstados.Denegado, f.Estado));
    }

    [Fact]
    public async Task filtro_por_metodo()
    {
        var (ctx, token, sedeId, idSocio) = await SembrarConcedidoAsync();
        var rHuella = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId); // huella
        await RetrocederAsync(ctx, rHuella.IdAcceso);
        var rManual = await ctx.AccesoService.RegistrarAccesoManualAsync(token, idSocio, sedeId); // manual
        await RetrocederAsync(ctx, rManual.IdAcceso);

        var manuales = await ctx.AccesoService.BuscarAsync(
            token, new AccesoHistorialFiltros { Metodo = AccesoMetodos.Manual }, idSedeFrontend: sedeId);

        Assert.True(manuales.TotalRegistros >= 1);
        Assert.All(manuales.Items, f => Assert.Equal(AccesoMetodos.Manual, f.Metodo));
    }

    [Fact]
    public async Task filtro_por_nombre_de_socio()
    {
        var (ctx, token, sedeId, idSocio) = await SembrarConcedidoAsync();
        await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId);

        var delSocio = await ctx.AccesoService.BuscarAsync(
            token, new AccesoHistorialFiltros { NombreSocio = "Juan" }, idSedeFrontend: sedeId);

        Assert.True(delSocio.TotalRegistros >= 1);
        Assert.All(delSocio.Items, f => Assert.Equal("Juan", f.NombreSocio));
    }

    [Fact]
    public async Task filtro_por_rango_de_fechas_excluye_fuera_de_rango()
    {
        var (ctx, token, sedeId, idSocio) = await SembrarConcedidoAsync();
        await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId);

        // Ventana futura: nada.
        var futuro = await ctx.AccesoService.BuscarAsync(
            token,
            new AccesoHistorialFiltros
            {
                Desde = "2999-01-01T00:00:00.000Z",
                Hasta = "2999-12-31T23:59:59.999Z",
            },
            idSedeFrontend: sedeId);
        Assert.Equal(0, futuro.TotalRegistros);

        // Ventana amplia desde 2000: todo.
        var todo = await ctx.AccesoService.BuscarAsync(
            token,
            new AccesoHistorialFiltros { Desde = "2000-01-01T00:00:00.000Z" },
            idSedeFrontend: sedeId);
        Assert.True(todo.TotalRegistros >= 1);
    }

    [Fact]
    public async Task paginacion()
    {
        var (ctx, token, sedeId, idSocio) = await SembrarConcedidoAsync();
        // La fila sembrada también sale de la ventana anti-doble-toque.
        await using var connSeed = ConnectionFactory.Open(ctx.DbPath);
        await connSeed.ExecuteAsync(new CommandDefinition(
            "UPDATE accesos_bitacora SET timestamp = @ts WHERE id_socio = @idSocio",
            new { ts = DateHelper.ToIsoUtc(DateTime.UtcNow.AddMinutes(-5)), idSocio }));

        for (var i = 0; i < 11; i++)
        {
            var r = await ctx.AccesoService.RegistrarAccesoManualAsync(token, idSocio, sedeId);
            await RetrocederAsync(ctx, r.IdAcceso);
        }

        var pagina1 = await ctx.AccesoService.BuscarAsync(token, pagina: 1, tamanoPagina: TamanosPagina.Default, idSedeFrontend: sedeId);
        Assert.Equal(11, pagina1.TotalRegistros);
        Assert.Equal(10, pagina1.Items.Count);
        Assert.Equal(2, pagina1.TotalPaginas);

        var pagina2 = await ctx.AccesoService.BuscarAsync(token, pagina: 2, tamanoPagina: TamanosPagina.Default, idSedeFrontend: sedeId);
        Assert.Single(pagina2.Items);
    }

    [Fact]
    public async Task aislamiento_por_sede()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);
        var otraSede = await Fase4Helper.InsertarSedeAsync(ctx);
        var idSocioOtra = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocioOtra, otraSede);
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, otraSede);
        var idPlan = await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = $"Plan-{UuidHelper.NewV4()[..8]}",
            DiasVigencia = 30,
            DiasCongelamientoMax = 0,
            PrecioCentavos = 5000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
        await ctx.MembresiasService.VenderAsync(token, idSocioOtra, idPlan, "efectivo", 5000, otraSede);
        await ctx.AccesoService.RegistrarAccesoManualAsync(token, idSocioOtra, otraSede);

        var mias = await ctx.AccesoService.BuscarAsync(token, idSedeFrontend: sedeId);
        Assert.Equal(0, mias.TotalRegistros);
    }
}
