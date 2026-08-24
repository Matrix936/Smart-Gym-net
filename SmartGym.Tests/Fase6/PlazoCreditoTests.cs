using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase6;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Fase6;

/// <summary>
/// Plazo de crédito configurable al vender (POS y membresías): la
/// fecha_vencimiento de la CuentaCobrar usa el plazo recibido en vez del valor
/// fijo. Null conserva el comportamiento histórico (POS 15 días / membresías
/// fecha_fin). Validación: entero entre 1 y 180. Además cubre la consulta de
/// deuda activa que alimenta el aviso del Kiosco.
/// </summary>
public sealed class PlazoCreditoTests
{
    private static async Task<(SecurityTestContext ctx, string token, long sedeId, long idProducto, string idSocio)> BaseCreditoAsync()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        await ctx.Configuracion.SetAsync("pos.permite_credito", "true");
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);
        return (ctx, token, sedeId, idProducto, idSocio);
    }

    [Fact]
    public async Task pos_venta_credito_con_plazo_custom_calcula_vencimiento()
    {
        var (ctx, token, sedeId, idProducto, idSocio) = await BaseCreditoAsync();

        await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            IdSocio = idSocio,
            MetodoPago = "efectivo",
            MontoPagadoCentavos = Fase6Helper.PrecioProteina - 20000,
            PlazoCreditoDias = 20,
        }, sedeId);

        var esperado = DateHelper.ToIsoUtc(DateTime.UtcNow.AddDays(20).Date);
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var fecha = await conn.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT fecha_vencimiento FROM cuentas_cobrar WHERE id_socio = @idSocio AND deleted_at IS NULL",
            new { idSocio }));
        Assert.NotNull(fecha);
        Assert.Equal(esperado[..10], fecha![..10]); // misma fecha (día), tolerando hora/formato fino
    }

    [Fact]
    public async Task pos_plazo_invalido_rechaza()
    {
        var (ctx, token, sedeId, idProducto, idSocio) = await BaseCreditoAsync();

        foreach (var plazo in new[] { 0, -3, 181 })
        {
            var ex = await Assert.ThrowsAsync<BusinessException>(
                () => ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
                {
                    Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
                    IdSocio = idSocio,
                    MetodoPago = "efectivo",
                    MontoPagadoCentavos = Fase6Helper.PrecioProteina - 20000,
                    PlazoCreditoDias = plazo,
                }, sedeId));
            Assert.Equal("plazo_invalido", ex.Code);
        }

        Assert.Equal(0, await ContarCuentasAsync(ctx));
    }

    [Fact]
    public async Task membresias_con_plazo_custom_vence_a_plazo_no_a_fecha_fin()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        var idPlan = await InsertarPlanAsync(ctx);

        // Con plazo 10: vence hoy + 10 (no junto con la membresía a 30 días).
        await ctx.MembresiasService.VenderAsync(
            token, idSocio, idPlan, "efectivo",
            montoRecibidoCentavos: 0, idSedeFrontend: sedeId, plazoCreditoDias: 10);

        var fechaConPlazo = await FechaVencimientoAsync(ctx, idSocio);
        Assert.Equal(Iso(DateTime.UtcNow.AddDays(10))[..10], fechaConPlazo![..10]);

        // Sin plazo (null): comportamiento histórico — vence junto con la
        // membresía de esa venta (renewal apilado: inicia cuando termina la
        // primera, por eso su fecha_fin es posterior).
        Assert.Equal(1, await ContarCuentasAsync(ctx));

        var fechaFinMembresia = await connQuery(ctx,
            "SELECT fecha_fin FROM membresias WHERE id_socio = @idSocio", new { idSocio });
        await ctx.MembresiasService.VenderAsync(
            token, idSocio, idPlan, "efectivo",
            montoRecibidoCentavos: 0, idSedeFrontend: sedeId);

        Assert.Equal(2, await ContarCuentasAsync(ctx));
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var fechas = (await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT fecha_vencimiento FROM cuentas_cobrar WHERE id_socio = @idSocio AND deleted_at IS NULL ORDER BY rowid",
            new { idSocio }))).ToList();
        var fechaFinSegunda = await connQuery(ctx,
            "SELECT fecha_fin FROM membresias WHERE id_socio = @idSocio ORDER BY rowid DESC LIMIT 1", new { idSocio });
        Assert.Equal(Iso(DateTime.UtcNow.AddDays(10))[..10], fechas[0][..10]);
        Assert.Equal(fechaFinSegunda![..10], fechas[1][..10]);
    }

    [Fact]
    public async Task kiosco_deuda_activa_detecta_pendiente_y_parcial()
    {
        var (ctx, token, sedeId, idProducto, idSocio) = await BaseCreditoAsync();

        Assert.False(await ctx.AccesoService.SocioTieneDeudaActivaAsync(idSocio));

        await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            IdSocio = idSocio,
            MetodoPago = "efectivo",
            MontoPagadoCentavos = Fase6Helper.PrecioProteina - 20000,
            PlazoCreditoDias = 30,
        }, sedeId);

        Assert.True(await ctx.AccesoService.SocioTieneDeudaActivaAsync(idSocio));

        // Socio sin cuentas: falso.
        var otro = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, otro, 1);
        Assert.False(await ctx.AccesoService.SocioTieneDeudaActivaAsync(otro));
    }

    private static async Task<long> InsertarPlanAsync(SecurityTestContext ctx)
    {
        return await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = $"Mensual-{UuidHelper.NewV4()[..8]}",
            DiasVigencia = 30,
            DiasCongelamientoMax = 7,
            PrecioCentavos = 50000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
    }

    private static async Task<string?> connQuery(SecurityTestContext ctx, string sql, object param)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        return await conn.ExecuteScalarAsync<string>(new CommandDefinition(sql, param));
    }

    private static async Task<int> ContarCuentasAsync(SecurityTestContext ctx)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM cuentas_cobrar WHERE deleted_at IS NULL"));
    }

    private static async Task<string?> FechaVencimientoAsync(SecurityTestContext ctx, string idSocio)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        return await conn.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT fecha_vencimiento FROM cuentas_cobrar WHERE id_socio = @idSocio AND deleted_at IS NULL",
            new { idSocio }));
    }

    private static string Iso(DateTime utc) =>
        utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
}