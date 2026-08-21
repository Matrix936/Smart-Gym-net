using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Membresias;

/// <summary>
/// PlanesMembresiaService: capa de sesión/permiso/validación que no existía
/// hasta este bloque (PlanesMembresiaRepository se usaba solo de lectura desde
/// MembresiasService.VenderAsync) — el CRUD administrativo del catálogo pasa
/// por aquí en vez de llamar al repositorio directo desde la UI.
/// </summary>
public sealed class PlanesMembresiaServiceTests
{
    [Fact]
    public async Task crear_plan_exitoso_queda_activo_y_recuperable()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();

        var plan = await ctx.PlanesService.CrearAsync(token, "Mensual", "Plan mensual", 30, 7, 50000);

        Assert.True(plan.IdPlan > 0);
        Assert.True(plan.EsActivo);

        var recuperado = await ctx.Planes.GetByIdAsync(plan.IdPlan);
        Assert.NotNull(recuperado);
        Assert.Equal("Mensual", recuperado!.Nombre);
        Assert.Equal(50000, recuperado.PrecioCentavos);
    }

    [Theory]
    [InlineData("", 30, 7, 50000, "nombre_obligatorio")]
    [InlineData("Mensual", 0, 7, 50000, "dias_vigencia_invalido")]
    [InlineData("Mensual", -5, 7, 50000, "dias_vigencia_invalido")]
    [InlineData("Mensual", 30, -1, 50000, "dias_congelamiento_invalido")]
    [InlineData("Mensual", 30, 7, -1, "precio_invalido")]
    public async Task crear_plan_con_datos_invalidos_es_rechazado(
        string nombre, int diasVigencia, int diasCongelamientoMax, long precioCentavos, string codigoEsperado)
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PlanesService.CrearAsync(token, nombre, null, diasVigencia, diasCongelamientoMax, precioCentavos));

        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal(codigoEsperado, ex.Code);
    }

    [Fact]
    public async Task editar_plan_existente_actualiza_los_campos()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();
        var plan = await ctx.PlanesService.CrearAsync(token, "Mensual", null, 30, 7, 50000);

        var editado = await ctx.PlanesService.EditarAsync(token, plan.IdPlan, "Mensual Promo", "Con descuento", 30, 10, 40000);

        Assert.Equal("Mensual Promo", editado.Nombre);
        Assert.Equal(40000, editado.PrecioCentavos);
        Assert.Equal(10, editado.DiasCongelamientoMax);
    }

    [Fact]
    public async Task editar_plan_inexistente_da_not_found()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PlanesService.EditarAsync(token, 999999, "X", null, 30, 7, 1000));

        Assert.Equal(BusinessError.NotFound, ex.Error);
        Assert.Equal("plan_no_encontrado", ex.Code);
    }

    [Fact]
    public async Task desactivar_plan_sigue_apareciendo_en_la_busqueda_como_inactivo()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();
        var plan = await ctx.PlanesService.CrearAsync(token, "Anual", null, 365, 15, 500000);

        await ctx.PlanesService.DesactivarAsync(token, plan.IdPlan);

        var resultado = await ctx.PlanesService.BuscarAsync(token);
        var encontrado = Assert.Single(resultado.Items, p => p.IdPlan == plan.IdPlan);
        Assert.False(encontrado.EsActivo);
    }

    [Fact]
    public async Task desactivar_un_plan_no_afecta_membresias_ya_vendidas_con_el()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var plan = await ctx.PlanesService.CrearAsync(token, "Mensual", null, 30, 7, 10000);
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Luz"), sedeId);
        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);
        var membresia = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, plan.IdPlan, Fase4Helper.MetodoPago, 10000, sedeId);

        await ctx.PlanesService.DesactivarAsync(token, plan.IdPlan);

        var membresiaTrasDesactivar = await ctx.Membresias.GetByIdAsync(membresia.IdMembresia);
        Assert.NotNull(membresiaTrasDesactivar);
        Assert.Equal(MembresiaEstados.Activa, membresiaTrasDesactivar!.Estado);
        Assert.Equal(plan.IdPlan, membresiaTrasDesactivar.IdPlan);
    }

    [Fact]
    public async Task activar_un_plan_desactivado_lo_vuelve_a_ofrecer()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();
        var plan = await ctx.PlanesService.CrearAsync(token, "Semestral", null, 180, 10, 200000);
        await ctx.PlanesService.DesactivarAsync(token, plan.IdPlan);

        await ctx.PlanesService.ActivarAsync(token, plan.IdPlan);

        var resultado = await ctx.PlanesService.BuscarAsync(token);
        var encontrado = Assert.Single(resultado.Items, p => p.IdPlan == plan.IdPlan);
        Assert.True(encontrado.EsActivo);
    }

    [Fact]
    public async Task buscar_devuelve_activos_e_inactivos_ordenados_con_activos_primero()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();
        var activo = await ctx.PlanesService.CrearAsync(token, "Activo", null, 30, 0, 1000);
        var inactivo = await ctx.PlanesService.CrearAsync(token, "Inactivo", null, 30, 0, 1000);
        await ctx.PlanesService.DesactivarAsync(token, inactivo.IdPlan);

        var resultado = await ctx.PlanesService.BuscarAsync(token);

        Assert.Equal(2, resultado.Items.Count);
        Assert.True(resultado.Items.First().EsActivo);
        Assert.Contains(resultado.Items, p => p.IdPlan == activo.IdPlan && p.EsActivo);
        Assert.Contains(resultado.Items, p => p.IdPlan == inactivo.IdPlan && !p.EsActivo);
    }

    [Fact]
    public async Task buscar_filtra_por_nombre_o_descripcion()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();
        await ctx.PlanesService.CrearAsync(token, "Mensual Básico", "Acceso a área de pesas", 30, 0, 1000);
        await ctx.PlanesService.CrearAsync(token, "Anual Premium", "Incluye clases grupales", 365, 15, 5000);

        var porNombre = await ctx.PlanesService.BuscarAsync(token, "mensual");
        Assert.Single(porNombre.Items);
        Assert.Equal("Mensual Básico", porNombre.Items[0].Nombre);

        var porDescripcion = await ctx.PlanesService.BuscarAsync(token, "clases grupales");
        Assert.Single(porDescripcion.Items);
        Assert.Equal("Anual Premium", porDescripcion.Items[0].Nombre);

        var sinCoincidencias = await ctx.PlanesService.BuscarAsync(token, "no existe");
        Assert.Empty(sinCoincidencias.Items);
    }

    [Fact]
    public async Task buscar_filtra_por_estado_activo_o_inactivo()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();
        var activo = await ctx.PlanesService.CrearAsync(token, "FiltroActivo", null, 30, 0, 1000);
        var inactivo = await ctx.PlanesService.CrearAsync(token, "FiltroInactivo", null, 30, 0, 1000);
        await ctx.PlanesService.DesactivarAsync(token, inactivo.IdPlan);

        var activos = await ctx.PlanesService.BuscarAsync(token, esActivo: true);
        var inactivos = await ctx.PlanesService.BuscarAsync(token, esActivo: false);

        Assert.Single(activos.Items, p => p.IdPlan == activo.IdPlan);
        Assert.All(activos.Items, p => Assert.True(p.EsActivo));
        Assert.Single(inactivos.Items, p => p.IdPlan == inactivo.IdPlan);
        Assert.All(inactivos.Items, p => Assert.False(p.EsActivo));

        // El filtro combina con la búsqueda por nombre.
        var combinado = await ctx.PlanesService.BuscarAsync(token, "FiltroInactivo", esActivo: false);
        Assert.Single(combinado.Items, p => p.IdPlan == inactivo.IdPlan);
    }

    [Fact]
    public async Task buscar_respeta_tamano_de_pagina_y_calcula_total_de_paginas()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();
        for (var i = 1; i <= 11; i++)
        {
            await ctx.PlanesService.CrearAsync(token, $"Plan {i:00}", null, 30, 0, 1000);
        }

        var pagina1 = await ctx.PlanesService.BuscarAsync(token, pagina: 1, tamanoPagina: TamanosPagina.Diez);
        Assert.Equal(10, pagina1.Items.Count);
        Assert.Equal(11, pagina1.TotalRegistros);
        Assert.Equal(2, pagina1.TotalPaginas);

        var pagina2 = await ctx.PlanesService.BuscarAsync(token, pagina: 2, tamanoPagina: TamanosPagina.Diez);
        Assert.Single(pagina2.Items);
    }

    [Fact]
    public async Task buscar_tamano_pagina_invalido_lanza_argument_exception()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => ctx.PlanesService.BuscarAsync(token, tamanoPagina: 7));
    }
}
