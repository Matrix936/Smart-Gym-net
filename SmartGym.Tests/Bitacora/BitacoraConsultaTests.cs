using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase5;
using SmartGym.Tests.Fase6;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Bitacora;

/// <summary>Consulta paginada/filtrada del historial de auditoría.</summary>
public sealed class BitacoraConsultaTests
{
    /// <summary>Genera acciones de 3 categorías distintas vía servicios reales.</summary>
    private static async Task<(SecurityTestContext ctx, string token, long sedeId, long idProducto)> SembrarAsync()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);
        await ctx.ProductosService.CrearAsync(
            token, "Producto Bitacora", 10000, null, true, 5, sedeId);
        await ctx.PlanesService.CrearAsync(token, "Plan Bitacora", null, 30, 0, 10000);
        return (ctx, token, sedeId, idProducto);
    }

    [Fact]
    public async Task buscar_devuelve_descendente_con_actor_y_sede_resueltos()
    {
        var (ctx, token, sedeId, _) = await SembrarAsync();

        var pagina = await ctx.BitacoraService.BuscarAsync(token, idSedeFrontend: sedeId);

        Assert.True(pagina.TotalRegistros >= 3);
        Assert.All(pagina.Items, f => Assert.False(string.IsNullOrEmpty(f.Fecha)));
        // La última acción sembrada (plan.creado, global) va primero: sede NULL
        // = visible desde cualquier sede, sin nombre de sede.
        Assert.Equal("plan.creado", pagina.Items[0].Accion);
        Assert.Null(pagina.Items[0].IdSede);
        Assert.Null(pagina.Items[0].SedeNombre);
        Assert.NotNull(pagina.Items[0].NombreUsuario);
    }

    [Fact]
    public async Task filtro_por_categoria_solo_trae_su_prefijo()
    {
        var (ctx, token, sedeId, _) = await SembrarAsync();

        var caja = await ctx.BitacoraService.BuscarAsync(
            token, new BitacoraFiltros { Categoria = "caja." }, idSedeFrontend: sedeId);

        Assert.Equal(1, caja.TotalRegistros);
        Assert.Equal("caja.abierta", caja.Items[0].Accion);

        var producto = await ctx.BitacoraService.BuscarAsync(
            token, new BitacoraFiltros { Categoria = "producto." }, idSedeFrontend: sedeId);

        Assert.Equal(1, producto.TotalRegistros);
        Assert.Equal("producto.creado", producto.Items[0].Accion);
    }

    [Fact]
    public async Task filtro_por_accion_exacta_trae_valor_nuevo_legible()
    {
        var (ctx, token, sedeId, _) = await SembrarAsync();

        var creado = await ctx.BitacoraService.BuscarAsync(
            token, new BitacoraFiltros { Accion = "producto.creado" }, idSedeFrontend: sedeId);

        var fila = Assert.Single(creado.Items);
        Assert.Contains("descripcion:Producto Bitacora", fila.ValorNuevo);
        Assert.Contains("precio:10000", fila.ValorNuevo);
        Assert.Null(fila.ValorAnterior);
    }

    [Fact]
    public async Task filtro_por_rango_de_fechas_excluye_lo_viejo()
    {
        var (ctx, token, sedeId, _) = await SembrarAsync();

        var soloFuturo = await ctx.BitacoraService.BuscarAsync(
            token,
            new BitacoraFiltros
            {
                Desde = "2999-01-01T00:00:00.000Z",
                Hasta = "2999-12-31T23:59:59.999Z",
            },
            idSedeFrontend: sedeId);
        Assert.Equal(0, soloFuturo.TotalRegistros);

        var todoElPasado = await ctx.BitacoraService.BuscarAsync(
            token,
            new BitacoraFiltros { Desde = "2000-01-01T00:00:00.000Z" },
            idSedeFrontend: sedeId);
        Assert.True(todoElPasado.TotalRegistros >= 3);
    }

    [Fact]
    public async Task paginacion_y_tamano_invalido()
    {
        var (ctx, token, sedeId, _) = await SembrarAsync();

        // 9 movimientos manuales + los ~3 ya sembrados superan una página de 10.
        for (var i = 0; i < 9; i++)
        {
            await ctx.CajaService.RegistrarMovimientoManualAsync(
                token, MovimientoTipos.Ingreso, $"fondo {i}", 1000, MetodosPago.Efectivo, sedeId);
        }

        var pagina1 = await ctx.BitacoraService.BuscarAsync(token, pagina: 1, tamanoPagina: 10, idSedeFrontend: sedeId);
        Assert.Equal(10, pagina1.Items.Count);
        Assert.True(pagina1.TotalRegistros >= 11);
        Assert.Equal(2, pagina1.TotalPaginas);

        var pagina2 = await ctx.BitacoraService.BuscarAsync(token, pagina: 2, tamanoPagina: 10, idSedeFrontend: sedeId);
        Assert.True(pagina2.Items.Count > 0);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            ctx.BitacoraService.BuscarAsync(token, tamanoPagina: 13, idSedeFrontend: sedeId));
    }

    [Fact]
    public async Task consultar_bitacora_sin_permiso_falla()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await Fase5Helper.ClearPermisosRolAsync(ctx);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.BitacoraService.BuscarAsync(token, idSedeFrontend: sedeId));

        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.Equal("sin_permiso", ex.Code);
    }
}
