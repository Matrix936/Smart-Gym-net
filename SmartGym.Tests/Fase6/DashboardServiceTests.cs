using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Data.Db;
using SmartGym.Data.Repositories;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase5;
using SmartGym.Tests.Fase6;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Fase6;

/// <summary>
/// Dashboard (/): resumen financiero con sede opcional (null = todas),
/// afluencia de accesos concedidos filtrada por sede, recordatorios de
/// membresía categorizados con MembresiaEstadoCalculator y plantillas
/// WhatsApp con defaults.
/// </summary>
public sealed class DashboardServiceTests
{
    private static string Iso(DateTime utc) =>
        utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Base + segunda sede activa con inventario y cajas abiertas en ambas.</summary>
    private static async Task<(SecurityTestContext ctx, string token, long sede1, long sede2, long idProducto)> BaseDosSedesAsync()
    {
        var (ctx, token, sede1, idProducto) = await Fase6Helper.BaseAsync();
        var sede2 = await ctx.Sedes.InsertAsync(new Sede
        {
            Nombre = "Sucursal Norte",
            EsActiva = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
        await Fase6Helper.InsertarInventarioAsync(ctx, idProducto, sede2, 10);
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sede1);
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sede2);
        return (ctx, token, sede1, sede2, idProducto);
    }

    // ---------------- Resumen ----------------

    [Fact]
    public async Task resumen_sin_sede_suma_todas_las_sedes()
    {
        var (ctx, token, sede1, sede2, idProducto) = await BaseDosSedesAsync();
        var v1 = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sede1);
        var v2 = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 2 }],
            MetodoPago = "efectivo",
        }, sede2);

        var esperadoTotal = v1.TotalCentavos + v2.TotalCentavos;

        var todas = await ctx.DashboardService.ObtenerResumenAsync(token, "hoy", idSedeFrontend: null);
        Assert.Equal(esperadoTotal, todas.IngresosCentavos);

        var soloSede1 = await ctx.DashboardService.ObtenerResumenAsync(token, "hoy", idSedeFrontend: sede1);
        Assert.Equal(v1.TotalCentavos, soloSede1.IngresosCentavos);
    }

    [Fact]
    public async Task resumen_sin_finanzas_ver_rechaza()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();
        await Fase5Helper.ClearPermisosRolAsync(ctx);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.DashboardService.ObtenerResumenAsync(token, "hoy"));
        Assert.Equal("sin_permiso", ex.Code);
    }

    // ---------------- Afluencia ----------------

    [Fact]
    public async Task afluencia_solo_concedidos_y_filtro_por_sede()
    {
        var (ctx, token, sede1, sede2, _) = await BaseDosSedesAsync();
        var plan = await InsertarPlanAsync(ctx);
        var socio1 = UuidHelper.NewV4();
        var socio2 = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, socio1, sede1);
        await Fase6Helper.InsertarSocioAsync(ctx, socio2, sede2);

        // Membresía vigente: sin ella el acceso manual es DENEGADO y no cuenta
        // como afluencia (por diseño — solo concedidos).
        await InsertarMembresiaAsync(ctx, socio1, plan, sede1, 30);
        await InsertarMembresiaAsync(ctx, socio2, plan, sede2, 30);

        await ctx.Accesos.RegistrarManualAsync(socio1, sede1, null);
        await ctx.Accesos.RegistrarManualAsync(socio2, sede2, null);

        var todas = await new DashboardRepository(ctx.DbPath).ObtenerAccesosConcedidosAsync(null, Iso(DateTime.UtcNow.AddDays(-1)));
        Assert.Equal(2, todas.Count);

        var soloSede1 = await new DashboardRepository(ctx.DbPath).ObtenerAccesosConcedidosAsync(sede1, Iso(DateTime.UtcNow.AddDays(-1)));
        Assert.Single(soloSede1);
    }

    // ---------------- Recordatorios ----------------

private static async Task SetTelefonoAsync(SecurityTestContext ctx, string idSocio, string telefono)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE socios SET telefono = @telefono WHERE id_socio = @idSocio",
            new { idSocio, telefono }));
    }
    private static async Task<long> InsertarPlanAsync(SecurityTestContext ctx)
    {
        return await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = $"Mensual-{UuidHelper.NewV4()[..8]}",
            DiasVigencia = 30,
            DiasCongelamientoMax = 7,
            PrecioCentavos = 10000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
    }

    private static async Task InsertarMembresiaAsync(
        SecurityTestContext ctx, string idSocio, long idPlan, long sedeId, int diasDesdeHoy)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO membresias (id_membresia, id_socio, id_plan, id_sede, fecha_inicio, fecha_fin, " +
            "estado, created_at, updated_at, sincronizado) " +
            "VALUES (@id, @idSocio, @idPlan, @idSede, @inicio, @fin, 'activa', @ahora, @ahora, 0)",
            new
            {
                id = UuidHelper.NewV4(),
                idSocio,
                idPlan,
                idSede = sedeId,
                inicio = Iso(DateTime.UtcNow.AddDays(-10)),
                fin = Iso(DateTime.UtcNow.AddDays(diasDesdeHoy)),
                ahora = DateHelper.NowIsoUtc(),
            }));
    }

    [Fact]
    public async Task recordatorios_categoriza_y_deduplica_por_socio()
    {
        var (ctx, token, sede1, _, _) = await BaseDosSedesAsync();
        var plan = await InsertarPlanAsync(ctx);

        // Socio A: vence en 3 días.
        var socioA = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, socioA, sede1);
        await SetTelefonoAsync(ctx, socioA, "+5215511111111");
        await InsertarMembresiaAsync(ctx, socioA, plan, sede1, 3);

        // Socio B: una vencida hace 5 días y otra por vencer en 2 — gana la por
        // vencer (prioridad) y solo queda UNA fila para el socio.
        var socioB = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, socioB, sede1);
        await SetTelefonoAsync(ctx, socioB, "+5215522222222");
        await InsertarMembresiaAsync(ctx, socioB, plan, sede1, -5);
        await InsertarMembresiaAsync(ctx, socioB, plan, sede1, 2);

        // Socio C: vence en 40 días — fuera del rango.
        var socioC = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, socioC, sede1);
        await InsertarMembresiaAsync(ctx, socioC, plan, sede1, 40);

        // Socio D: sin teléfono — no es contactable, se excluye.
        var socioD = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, socioD, sede1);
        await InsertarMembresiaAsync(ctx, socioD, plan, sede1, 4);

        var recordatorios = (await ctx.DashboardService.ObtenerRecordatoriosAsync(token)).ToList();

        Assert.Equal(2, recordatorios.Count);

        var a = Assert.Single(recordatorios, r => r.IdSocio == socioA);
        Assert.Equal(RecordatorioCategorias.PorVencer, a.Categoria);
        Assert.Equal(3, a.Dias);

        var b = Assert.Single(recordatorios, r => r.IdSocio == socioB);
        Assert.Equal(RecordatorioCategorias.PorVencer, b.Categoria);
        Assert.Equal(2, b.Dias);
    }

    [Fact]
    public async Task recordatorios_vencida_dentro_del_maximo()
    {
        var (ctx, token, sede1, _, _) = await BaseDosSedesAsync();
        var plan = await InsertarPlanAsync(ctx);

        var socio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, socio, sede1);
        await SetTelefonoAsync(ctx, socio, "+5215533333333");
        await InsertarMembresiaAsync(ctx, socio, plan, sede1, -12);

        var r = Assert.Single(await ctx.DashboardService.ObtenerRecordatoriosAsync(token));
        Assert.Equal(RecordatorioCategorias.Vencida, r.Categoria);
        Assert.Equal(12, r.Dias);
    }

    // ---------------- Plantillas WhatsApp ----------------

    [Fact]
    public async Task plantillas_whatsapp_default_y_personalizada()
    {
        var (ctx, token, _, _, _) = await BaseDosSedesAsync();

        var (porVencerDefault, vencidaDefault) = await ctx.DashboardService.ObtenerPlantillasWhatsAppAsync();
        Assert.Contains("{nombre}", porVencerDefault);
        Assert.Contains("{dias}", vencidaDefault);

        await ctx.DashboardService.GuardarPlantillasWhatsAppAsync(
            token, "Hola {nombre}, vence en {dias}.", "");
        var (porVencer, vencida) = await ctx.DashboardService.ObtenerPlantillasWhatsAppAsync();
        Assert.Equal("Hola {nombre}, vence en {dias}.", porVencer);
        Assert.Equal(vencidaDefault, vencida); // vacío vuelve al default

        // Persistencia real en configuracion_general.
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var valor = await conn.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT valor FROM configuracion_general WHERE clave = 'whatsapp.plantilla.por_vencer'"));
        Assert.Equal("Hola {nombre}, vence en {dias}.", valor);
    }
}
