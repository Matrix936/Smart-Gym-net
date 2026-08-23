using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase5;
using SmartGym.Tests.Fase6;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Data;

/// <summary>
/// Fix transversal: búsqueda insensible a acentos vía función SQL sin_acentos()
/// (ConnectionFactory). Un caso por repositorio: buscar SIN acento encuentra el
/// registro CON acento, y viceversa (la normalización es simétrica).
/// </summary>
public sealed class BusquedaSinAcentosTests
{
    [Fact]
    public async Task socios_buscar_gomez_encuentra_gomez_con_acento()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(
            token, Fase4Helper.DatosSocio("Rodrigo Gómez"), sedeId);

        var sinAcento = await ctx.SociosService.BuscarAsync(token, "gomez");
        Assert.Contains(sinAcento.Items, s => s.IdSocio == socio.IdSocio);

        // Viceversa: con acento también lo encuentra.
        var conAcento = await ctx.SociosService.BuscarAsync(token, "Gómez");
        Assert.Contains(conAcento.Items, s => s.IdSocio == socio.IdSocio);
    }

    [Fact]
    public async Task planes_buscar_basico_encuentra_basica()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        var plan = await ctx.PlanesService.CrearAsync(token, "Plan Básico Mensual", null, 30, 0, 10000);

        var sinAcento = await ctx.PlanesService.BuscarAsync(token, "basico mensual");
        Assert.Contains(sinAcento.Items, p => p.IdPlan == plan.IdPlan);

        var conAcento = await ctx.PlanesService.BuscarAsync(token, "básico mensual");
        Assert.Contains(conAcento.Items, p => p.IdPlan == plan.IdPlan);
    }

    [Fact]
    public async Task productos_buscar_cafe_encuentra_cafe_con_acento()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        var producto = await ctx.ProductosService.CrearAsync(
            token, "Café en grano", 25000, null, false, 0, sedeId);

        var sinAcento = await ctx.ProductosService.BuscarAsync(token, "cafe en grano");
        Assert.Contains(sinAcento.Items, p => p.IdProducto == producto.IdProducto);

        var conAcento = await ctx.ProductosService.BuscarAsync(token, "café");
        Assert.Contains(conAcento.Items, p => p.IdProducto == producto.IdProducto);
    }

    [Fact]
    public async Task maquinaria_buscar_maquina_encuentra_maquina_con_acento()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var maquina = await ctx.MaquinariaService.CrearAsync(
            token, "Máquina de caminar", null, MaquinaEstados.Funcionando, null, sedeId);
        _ = idProducto;

        var sinAcento = await ctx.MaquinariaService.BuscarAsync(token, nombre: "maquina de caminar", idSedeFrontend: sedeId);
        Assert.Contains(sinAcento.Items, m => m.IdMaquina == maquina.IdMaquina);

        var conAcento = await ctx.MaquinariaService.BuscarAsync(token, nombre: "máquina", idSedeFrontend: sedeId);
        Assert.Contains(conAcento.Items, m => m.IdMaquina == maquina.IdMaquina);
    }
}
