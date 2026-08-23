using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase5;
using SmartGym.Tests.Fase6;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Finanzas;

/// <summary>Dashboard de Finanzas: agregación sobre caja_movimientos + métricas de membresías.</summary>
public sealed class FinanzasTests
{
    /// <summary>Sembrador: caja abierta, venta POS efectivo, membresía vendida (efectivo) y abono.</summary>
    private static async Task<(SecurityTestContext ctx, string token, long sedeId, long idProducto)> SembrarAsync()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        // Venta POS: 2 x 50000 = 100000 (ingreso 'venta').
        await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 2 }],
            MetodoPago = "efectivo",
        }, sedeId);

        // Membresía: plan de 10000 vendido en efectivo (ingreso 'pago_membresia').
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        var idPlan = await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = $"Plan Finanzas-{UuidHelper.NewV4()[..8]}",
            DiasVigencia = 30,
            DiasCongelamientoMax = 0,
            PrecioCentavos = 10000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
        await ctx.MembresiasService.VenderAsync(token, idSocio, idPlan, "efectivo", 10000, sedeId);

        return (ctx, token, sedeId, idProducto);
    }

    [Fact]
    public async Task dashboard_totaliza_y_desglosa_por_tipo()
    {
        var (ctx, token, sedeId, _) = await SembrarAsync();

        var dashboard = await ctx.FinanzasService.ObtenerDashboardAsync(
            token, "2000-01-01T00:00:00.000Z", "2999-12-31T23:59:59.999Z", sedeId);

        // Ingresos: venta 100000 + membresía 10000.
        Assert.Equal(110000, dashboard.Actual.IngresosCentavos);
        Assert.Equal(100000, dashboard.Actual.IngresosProductos);
        Assert.Equal(10000, dashboard.Actual.IngresosMembresias);
        Assert.Equal(0, dashboard.Actual.EgresosCentavos);
        Assert.Equal(110000, dashboard.Actual.NetoCentavos);
        Assert.True(dashboard.Actual.SerieDiaria.Count >= 1);
    }

    [Fact]
    public async Task cancelacion_de_venta_aparece_como_egreso_y_afecta_neto()
    {
        var (ctx, token, sedeId, idProducto) = await SembrarAsync();

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "tarjeta",
        }, sedeId);
        await ctx.PosService.CancelarVentaAsync(token, new CancelarVentaInput
        {
            IdVenta = venta.IdVenta,
            PasswordConfirmacion = Fase4Helper.Password,
        }, sedeId);

        var dashboard = await ctx.FinanzasService.ObtenerDashboardAsync(
            token, "2000-01-01T00:00:00.000Z", "2999-12-31T23:59:59.999Z", sedeId);

        // La cancelación genera un egreso por el mismo monto de la venta.
        Assert.Equal(Fase6Helper.PrecioProteina, dashboard.Actual.EgresosCentavos);
        Assert.Equal(dashboard.Actual.IngresosCentavos - dashboard.Actual.EgresosCentavos, dashboard.Actual.NetoCentavos);
    }

    [Fact]
    public async Task periodo_anterior_vacio_da_cero_y_el_actual_no_contamina()
    {
        var (ctx, token, sedeId, _) = await SembrarAsync();

        // Rango de UN día que contiene los movimientos vs. el día inmediatamente anterior (vacío).
        var hoyUtc = DateTime.UtcNow.Date;
        var desdeActual = ToIso(hoyUtc);
        var hastaActual = ToIso(hoyUtc.AddDays(1).AddMilliseconds(-1));

        var dashboard = await ctx.FinanzasService.ObtenerDashboardAsync(
            token, desdeActual, hastaActual, sedeId);

        Assert.True(dashboard.Actual.IngresosCentavos > 0);
        Assert.Equal(0, dashboard.IngresosPeriodoAnterior);
        Assert.Equal(0, dashboard.NetoPeriodoAnterior);
    }

    [Fact]
    public async Task socios_activos_usa_estado_efectivo_y_contea_distintos()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        // Dos membresías activas DEL MISMO socio → socios activos = 1.
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        foreach (var nombre in new[] { "Plan A", "Plan B" })
        {
            var idPlan = await ctx.Planes.InsertAsync(new PlanMembresia
            {
                Nombre = $"{nombre}-{UuidHelper.NewV4()[..8]}",
                DiasVigencia = 30,
                DiasCongelamientoMax = 0,
                PrecioCentavos = 5000,
                EsActivo = true,
                UpdatedAt = DateHelper.NowIsoUtc(),
            });
            await ctx.MembresiasService.VenderAsync(token, idSocio, idPlan, "efectivo", 5000, sedeId);
        }

        var dashboard = await ctx.FinanzasService.ObtenerDashboardAsync(
            token, "2000-01-01T00:00:00.000Z", "2999-12-31T23:59:59.999Z", sedeId);

        Assert.Equal(1, dashboard.SociosActivos);
        Assert.True(dashboard.MembresiasNuevas >= 2);
    }

    [Fact]
    public async Task ver_finanzas_sin_permiso_falla()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await Fase5Helper.ClearPermisosRolAsync(ctx);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.FinanzasService.ObtenerDashboardAsync(token, "2000-01-01T00:00:00.000Z", "2999-12-31T23:59:59.999Z", sedeId));
        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.Equal("sin_permiso", ex.Code);
    }

    private static string ToIso(DateTime utc) =>
        utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
}
