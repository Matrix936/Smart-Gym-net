using SmartGym.Core.Common;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Printing;

/// <summary>
/// Configuración de periféricos del POS (claves perifericos.* + pos.imprimir_auto,
/// impresora reutiliza impresora.nombre): defaults, round-trip y normalización.
/// </summary>
public sealed class PerifericosTicketConfigTests
{
    [Fact]
    public async Task sin_claves_previas_devuelve_defaults()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();

        var config = await ctx.EmpresaConfigService.ObtenerPerifericosAsync(token);

        Assert.Null(config.ImpresoraTickets);
        Assert.Equal(42, config.PapelAncho);
        Assert.Equal(2, config.Densidad);
        Assert.False(config.AbrirCajon);
        Assert.False(config.ImprimirAutoAlCobrar);
    }

    [Fact]
    public async Task guardar_y_leer_hace_round_trip()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();

        var guardada = await ctx.EmpresaConfigService.GuardarPerifericosAsync(token, new PerifericosTicket
        {
            ImpresoraTickets = "  EPSON TM-T20III  ",
            PapelAncho = 32,
            Densidad = 3,
            AbrirCajon = true,
            ImprimirAutoAlCobrar = true,
        });

        Assert.Equal("EPSON TM-T20III", guardada.ImpresoraTickets);

        var leida = await ctx.EmpresaConfigService.ObtenerPerifericosAsync(token);
        Assert.Equal("EPSON TM-T20III", leida.ImpresoraTickets);
        Assert.Equal(32, leida.PapelAncho);
        Assert.Equal(3, leida.Densidad);
        Assert.True(leida.AbrirCajon);
        Assert.True(leida.ImprimirAutoAlCobrar);
    }

    [Fact]
    public async Task valores_fuera_de_rango_se_normalizan_al_default()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();

        var guardada = await ctx.EmpresaConfigService.GuardarPerifericosAsync(token, new PerifericosTicket
        {
            ImpresoraTickets = "   ",
            PapelAncho = 80,
            Densidad = 9,
        });

        Assert.Null(guardada.ImpresoraTickets);
        Assert.Equal(42, guardada.PapelAncho);
        Assert.Equal(3, guardada.Densidad); // clamp, no default

        // Y lo persistido normalizado es lo que se vuelve a leer.
        var leida = await ctx.EmpresaConfigService.ObtenerPerifericosAsync(token);
        Assert.Equal(42, leida.PapelAncho);
        Assert.Equal(3, leida.Densidad);
    }

    [Fact]
    public async Task impresora_historica_impresora_nombre_es_la_de_tickets()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();
        await ctx.EmpresaConfigService.GuardarImpresoraAsync(token, "TM-T20");

        var config = await ctx.EmpresaConfigService.ObtenerPerifericosAsync(token);
        Assert.Equal("TM-T20", config.ImpresoraTickets);
    }
}
