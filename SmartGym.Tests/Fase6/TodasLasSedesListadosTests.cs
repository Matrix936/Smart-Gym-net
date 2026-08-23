using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase5;
using SmartGym.Tests.Fase6;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Fase6;

/// <summary>
/// "Todas las sedes" en los 4 listados: idSedeFrontend null (filtro de sede
/// apagado en la UI) devuelve filas de TODAS las sedes vía
/// ResolverIdSedeOpcionalAsync; con sede específica, solo esa. Las escrituras
/// siguen usando el resolver estricto y exigen sede concreta sin excepción.
/// </summary>
public sealed class TodasLasSedesListadosTests
{
    /// <summary>Base + segunda sede activa con inventario para vender POS.</summary>
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
        return (ctx, token, sede1, sede2, idProducto);
    }

    private static async Task AbrirCajasAsync(SecurityTestContext ctx, string token, long sede1, long sede2)
    {
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sede1);
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sede2);
    }

    /// <summary>Vende una membresía real en la sede indicada (caja debe estar abierta).</summary>
    private static async Task VenderMembresiaAsync(SecurityTestContext ctx, string token, long sedeId)
    {
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        var idPlan = await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = $"Mensual-{UuidHelper.NewV4()[..8]}",
            DiasVigencia = 30,
            DiasCongelamientoMax = 7,
            PrecioCentavos = 10000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
        await ctx.MembresiasService.VenderAsync(token, idSocio, idPlan, "efectivo", 10000, sedeId);
    }

    private static async Task<(string idVenta1, string idVenta2)> VenderPosEnAmbasAsync(
        SecurityTestContext ctx, string token, long sede1, long sede2, long idProducto)
    {
        var v1 = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sede1);
        var v2 = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sede2);
        return (v1.IdVenta, v2.IdVenta);
    }

    // ---------------- Ventas ----------------

    [Fact]
    public async Task ventas_historial_sin_sede_devuelve_todas_las_sedes()
    {
        var (ctx, token, sede1, sede2, idProducto) = await BaseDosSedesAsync();
        await AbrirCajasAsync(ctx, token, sede1, sede2);
        await VenderPosEnAmbasAsync(ctx, token, sede1, sede2, idProducto);

        var filtros = new HistorialFiltros { TipoReferencia = CajaReferenciaTipos.Venta };

        var conSede1 = await ctx.VentasService.BuscarHistorialAsync(token, filtros, idSedeFrontend: sede1);
        var conSede2 = await ctx.VentasService.BuscarHistorialAsync(token, filtros, idSedeFrontend: sede2);
        var todas = await ctx.VentasService.BuscarHistorialAsync(token, filtros, idSedeFrontend: null);
        System.Console.WriteLine($"DIAG sede1={conSede1.TotalRegistros} sede2={conSede2.TotalRegistros} null={todas.TotalRegistros}");
        Assert.Equal(2, todas.TotalRegistros);
        Assert.Contains(todas.Items, i => i.IdSede == sede1);
        Assert.Contains(todas.Items, i => i.IdSede == sede2);

        var soloSede1 = await ctx.VentasService.BuscarHistorialAsync(token, filtros, idSedeFrontend: sede1);
        var fila = Assert.Single(soloSede1.Items);
        Assert.Equal(sede1, fila.IdSede);
    }

    // ---------------- Membresías ----------------

    [Fact]
    public async Task membresias_buscar_sin_sede_devuelve_todas_las_sedes()
    {
        var (ctx, token, sede1, sede2, _) = await BaseDosSedesAsync();
        await AbrirCajasAsync(ctx, token, sede1, sede2);
        await VenderMembresiaAsync(ctx, token, sede1);
        await VenderMembresiaAsync(ctx, token, sede2);

        var todas = await ctx.MembresiasService.BuscarAsync(token, idSedeFrontend: null);
        Assert.Equal(2, todas.TotalRegistros);

        var soloSede1 = await ctx.MembresiasService.BuscarAsync(token, idSedeFrontend: sede1);
        Assert.Equal(1, soloSede1.TotalRegistros);
    }

    // ---------------- Bitácora ----------------

    [Fact]
    public async Task bitacora_buscar_sin_sede_incluye_todas_las_sedes()
    {
        var (ctx, token, sede1, sede2, _) = await BaseDosSedesAsync();
        await AbrirCajasAsync(ctx, token, sede1, sede2);
        await VenderMembresiaAsync(ctx, token, sede1);
        await VenderMembresiaAsync(ctx, token, sede2);

        var todas = await ctx.BitacoraService.BuscarAsync(token, idSedeFrontend: null);
        Assert.Contains(todas.Items, i => i.IdSede == sede1);
        Assert.Contains(todas.Items, i => i.IdSede == sede2);

        var soloSede2 = await ctx.BitacoraService.BuscarAsync(token, idSedeFrontend: sede2);
        Assert.All(soloSede2.Items, i => Assert.True(i.IdSede is null || i.IdSede == sede2));
    }

    // ---------------- Historial de acceso ----------------

    [Fact]
    public async Task accesos_historial_sin_sede_devuelve_todas_las_sedes()
    {
        var (ctx, token, sede1, sede2, _) = await BaseDosSedesAsync();

        var socio1 = UuidHelper.NewV4();
        var socio2 = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, socio1, sede1);
        await Fase6Helper.InsertarSocioAsync(ctx, socio2, sede2);
        await ctx.Accesos.RegistrarManualAsync(socio1, sede1, null);
        await ctx.Accesos.RegistrarManualAsync(socio2, sede2, null);

        var todas = await ctx.AccesoService.BuscarAsync(token, idSedeFrontend: null);
        Assert.Equal(2, todas.TotalRegistros);

        var soloSede1 = await ctx.AccesoService.BuscarAsync(token, idSedeFrontend: sede1);
        var fila = Assert.Single(soloSede1.Items);
        Assert.Equal(socio1, fila.IdSocio);
    }

    // ---------------- Escrituras: el resolver estricto no cambia ----------------

    [Fact]
    public async Task escrituras_sin_sede_concreta_siguen_rechazando()
    {
        var (ctx, token, _, _, idProducto) = await BaseDosSedesAsync();

        var exCaja = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CajaService.AbrirCajaAsync(token, 100000, idSedeFrontend: null));
        Assert.Equal("sede_requerida", exCaja.Code);

        var exPos = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
            {
                Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
                MetodoPago = "efectivo",
            }, idSedeFrontend: null));
        Assert.Equal("sede_requerida", exPos.Code);

        // VenderAsync valida socio y plan ANTES de resolver la sede: usamos
        // datos reales para que el rechazo venga del resolver estricto.
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, 1);
        var idPlanReal = await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = $"Mensual-{UuidHelper.NewV4()[..8]}",
            DiasVigencia = 30,
            DiasCongelamientoMax = 7,
            PrecioCentavos = 10000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
        var exVentaMem = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.MembresiasService.VenderAsync(
                token, idSocio, idPlanReal, "efectivo", 10000, idSedeFrontend: null));
        Assert.Equal("sede_requerida", exVentaMem.Code);
    }
}
