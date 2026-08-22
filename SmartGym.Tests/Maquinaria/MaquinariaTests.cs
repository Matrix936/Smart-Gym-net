using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase5;
using SmartGym.Tests.Fase6;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Maquinaria;

/// <summary>Catálogo de maquinaria por sede: CRUD, estados y bitácora.</summary>
public sealed class MaquinariaTests
{
    [Fact]
    public async Task crear_maquina_con_sede_resuelta_y_bitacora()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();

        var maquina = await ctx.MaquinariaService.CrearAsync(
            token, "Caminadora 1", "Caminadora eléctrica", MaquinaEstados.Funcionando,
            notas: "Recién comprada", sedeId);

        Assert.False(string.IsNullOrEmpty(maquina.IdMaquina));
        Assert.Equal(sedeId, maquina.IdSede);
        Assert.Equal(MaquinaEstados.Funcionando, maquina.Estado);
        Assert.True(maquina.EsActivo);

        // Bitácora registrada (patrón AuditoriaTests).
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var valorNuevo = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT valor_nuevo FROM bitacora_auditoria WHERE accion = 'maquina.creada'"));
        Assert.Contains("nombre:Caminadora 1", valorNuevo);
    }

    [Fact]
    public async Task crear_con_nombre_vacio_o_estado_invalido_da_validation()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();

        var exNombre = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.MaquinariaService.CrearAsync(token, "   ", null, MaquinaEstados.Funcionando, null, sedeId));
        Assert.Equal("nombre_obligatorio", exNombre.Code);

        var exEstado = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.MaquinariaService.CrearAsync(token, "X", null, "rota", null, sedeId));
        Assert.Equal("estado_invalido", exEstado.Code);
    }

    [Fact]
    public async Task editar_modifica_datos_persistidos()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        var maquina = await ctx.MaquinariaService.CrearAsync(
            token, "Banco viejo", null, MaquinaEstados.Funcionando, null, sedeId);

        var editada = await ctx.MaquinariaService.EditarAsync(
            token, maquina.IdMaquina, "Banco renovado", "Con nuevas almohadillas", "Garantía vigente");

        Assert.Equal("Banco renovado", editada.Nombre);
        Assert.Equal("Con nuevas almohadillas", editada.Descripcion);
    }

    [Fact]
    public async Task cambiar_estado_registra_anterior_y_nuevo()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        var maquina = await ctx.MaquinariaService.CrearAsync(
            token, "Prensa", null, MaquinaEstados.Funcionando, null, sedeId);

        var cambiada = await ctx.MaquinariaService.CambiarEstadoAsync(
            token, maquina.IdMaquina, MaquinaEstados.EnMantenimiento);

        Assert.Equal(MaquinaEstados.EnMantenimiento, cambiada.Estado);

        var exMismo = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.MaquinariaService.CambiarEstadoAsync(
                token, maquina.IdMaquina, MaquinaEstados.EnMantenimiento));
        Assert.Equal("mismo_estado", exMismo.Code);

        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var fila = await conn.QuerySingleAsync<(string ValorAnterior, string ValorNuevo)>(new CommandDefinition(
            "SELECT valor_anterior, valor_nuevo FROM bitacora_auditoria WHERE accion = 'maquina.estado_cambiado'"));
        Assert.Equal(MaquinaEstados.Funcionando, fila.ValorAnterior);
        Assert.Equal(MaquinaEstados.EnMantenimiento, fila.ValorNuevo);
    }

    [Fact]
    public async Task desactivar_quita_del_listado_activo_y_activar_restaura()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        var maquina = await ctx.MaquinariaService.CrearAsync(
            token, "Elíptica", null, MaquinaEstados.Funcionando, null, sedeId);

        await ctx.MaquinariaService.DesactivarAsync(token, maquina.IdMaquina);

        Assert.Null(await ctx.Maquinaria.GetByIdAsync(maquina.IdMaquina));
        Assert.NotNull(await ctx.Maquinaria.GetByIdCualquierEstadoAsync(maquina.IdMaquina));

        var inactivas = await ctx.MaquinariaService.BuscarAsync(
            token, esActivo: false, idSedeFrontend: sedeId);
        Assert.Single(inactivas.Items);

        await ctx.MaquinariaService.ActivarAsync(token, maquina.IdMaquina);
        Assert.NotNull(await ctx.Maquinaria.GetByIdAsync(maquina.IdMaquina));
    }

    [Fact]
    public async Task buscar_filtra_por_nombre_estado_y_pagina()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await ctx.MaquinariaService.CrearAsync(token, "Caminadora A", null, MaquinaEstados.Funcionando, null, sedeId);
        await ctx.MaquinariaService.CrearAsync(token, "Caminadora B", null, MaquinaEstados.EnMantenimiento, null, sedeId);
        await ctx.MaquinariaService.CrearAsync(token, "Mancuernas 10kg", null, MaquinaEstados.Funcionando, null, sedeId);

        var caminadoras = await ctx.MaquinariaService.BuscarAsync(token, nombre: "caminadora", idSedeFrontend: sedeId);
        Assert.Equal(2, caminadoras.TotalRegistros);

        var enMantenimiento = await ctx.MaquinariaService.BuscarAsync(
            token, estado: MaquinaEstados.EnMantenimiento, idSedeFrontend: sedeId);
        var fila = Assert.Single(enMantenimiento.Items);
        Assert.Equal("Caminadora B", fila.Nombre);

        var pagina1 = await ctx.MaquinariaService.BuscarAsync(token, pagina: 1, tamanoPagina: TamanosPagina.Default, idSedeFrontend: sedeId);
        Assert.Equal(3, pagina1.TotalRegistros);
        Assert.True(pagina1.TotalPaginas >= 1);
    }

    [Fact]
    public async Task maquinas_de_otra_sede_no_aparecen()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await ctx.MaquinariaService.CrearAsync(token, "Solo Sede 1", null, MaquinaEstados.Funcionando, null, sedeId);
        var otraSede = await Fase4Helper.InsertarSedeAsync(ctx);
        await ctx.MaquinariaService.CrearAsync(token, "Solo Otra Sede", null, MaquinaEstados.Funcionando, null, otraSede);

        var deMiSede = await ctx.MaquinariaService.BuscarAsync(token, idSedeFrontend: sedeId);
        Assert.Single(deMiSede.Items);
        Assert.Equal("Solo Sede 1", deMiSede.Items[0].Nombre);
    }

    [Fact]
    public async Task gestionar_maquinaria_sin_permiso_falla()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await Fase5Helper.ClearPermisosRolAsync(ctx);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.MaquinariaService.BuscarAsync(token, idSedeFrontend: sedeId));
        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.Equal("sin_permiso", ex.Code);
    }
}
