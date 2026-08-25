using Dapper;
using SmartGym.Core.Common;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Fase6;

/// <summary>
/// Panel de cobranza vencida del Dashboard: query de cuentas por cobrar
/// VENCIDAS (pendiente/parcial con fecha_vencimiento pasada) con datos de
/// contacto, y plantilla WhatsApp configurable con {nombre} y {monto}.
/// </summary>
public sealed class CobranzaVencidaDashboardTests
{
    private static async Task SembrarCuentaAsync(
        SecurityTestContext ctx, string idSocio, long idSede,
        long saldoCentavos, int diasVencimiento, string estado)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var vencimiento = DateTime.UtcNow.Date.AddDays(diasVencimiento);
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO cuentas_cobrar (id_cuenta, id_membresia, origen, id_socio, saldo_pendiente_centavos, " +
            "fecha_vencimiento, estado, updated_at, sincronizado) " +
            "VALUES (@id, NULL, 'pos', @idSocio, @saldo, @vencimiento, @estado, @ahora, 0)",
            new
            {
                id = UuidHelper.NewV4(),
                idSocio,
                saldo = saldoCentavos,
                vencimiento = DateHelper.ToIsoUtc(vencimiento),
                estado,
                ahora = DateHelper.NowIsoUtc(),
            }));
    }

    [Fact]
    public async Task lista_solo_vencidas_pendientes_o_parciales()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        var socioA = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, socioA, sedeId);
        await SembrarCuentaAsync(ctx, socioA, sedeId, saldoCentavos: 20000, diasVencimiento: -3, estado: "pendiente");

        var socioB = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, socioB, sedeId);
        await SembrarCuentaAsync(ctx, socioB, sedeId, saldoCentavos: 5000, diasVencimiento: -10, estado: "parcial");

        // Excluidos: futura pendiente, vencida ya cobrada, vencida incobrable.
        var socioFutura = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, socioFutura, sedeId);
        await SembrarCuentaAsync(ctx, socioFutura, sedeId, saldoCentavos: 1000, diasVencimiento: 20, estado: "pendiente");

        var socioCobrada = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, socioCobrada, sedeId);
        await SembrarCuentaAsync(ctx, socioCobrada, sedeId, saldoCentavos: 7000, diasVencimiento: -5, estado: "cobrada");

        var vencidas = (await ctx.DashboardService.ObtenerCobranzaVencidaAsync(token)).Items.ToList();

        Assert.Equal(2, vencidas.Count);
        Assert.All(vencidas, v => Assert.True(v.SaldoPendienteCentavos > 0));
        // La más vencida primero.
        Assert.Equal(socioB, vencidas[0].IdSocio);
        Assert.Equal(10, vencidas[0].DiasVencido);
        Assert.Equal(socioA, vencidas[1].IdSocio);
        Assert.Equal(3, vencidas[1].DiasVencido);
    }

    [Fact]
    public async Task incluye_telefono_del_socio_para_whatsapp()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        await using var connTel = ConnectionFactory.Open(ctx.DbPath);
        await connTel.ExecuteAsync(new CommandDefinition(
            "UPDATE socios SET telefono = '+5215500000000' WHERE id_socio = @idSocio",
            new { idSocio }));

        var vencimientoPasado = DateHelper.ToIsoUtc(DateTime.UtcNow.Date.AddDays(-2).AddHours(15));
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO cuentas_cobrar (id_cuenta, id_membresia, origen, id_socio, saldo_pendiente_centavos, " +
            "fecha_vencimiento, estado, updated_at, sincronizado) " +
            "VALUES (@id, NULL, 'pos', @idSocio, 30000, @vencimiento, 'parcial', @ahora, 0)",
            new { id = UuidHelper.NewV4(), idSocio, vencimiento = vencimientoPasado, ahora = DateHelper.NowIsoUtc() }));

        var vencidas = (await ctx.DashboardService.ObtenerCobranzaVencidaAsync(token)).Items.ToList();

        var dto = Assert.Single(vencidas);
        Assert.Equal("+5215500000000", dto.Telefono);
    }

    [Fact]
    public async Task plantilla_cobranza_default_personalizada_y_persistencia()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();

        var plantillasDefault = await ctx.DashboardService.ObtenerPlantillasWhatsAppAsync();
        var cobranzaDefault = plantillasDefault.Cobranza;
        Assert.Contains("{nombre}", cobranzaDefault);
        Assert.Contains("{monto}", cobranzaDefault);

        await ctx.DashboardService.GuardarPlantillasWhatsAppAsync(
            token, "", "", "Saldo {monto} pendiente, {nombre}.");
        var plantillasGuardadas = await ctx.DashboardService.ObtenerPlantillasWhatsAppAsync();
        var cobranza = plantillasGuardadas.Cobranza;
        Assert.Equal("Saldo {monto} pendiente, {nombre}.", cobranza);

        // Persistencia real en configuracion_general.
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var valor = await conn.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT valor FROM configuracion_general WHERE clave = 'whatsapp.plantilla.cobranza_vencida'"));
        Assert.Equal("Saldo {monto} pendiente, {nombre}.", valor);
    }
}
