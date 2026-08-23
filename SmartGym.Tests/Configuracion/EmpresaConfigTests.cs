using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Errors;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase5;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Configuracion;

/// <summary>Configuracion post-setup: datos de empresa, logo e impresora.</summary>
public sealed class EmpresaConfigTests
{
    [Fact]
    public async Task actualizar_datos_guarda_y_registra_bitacora()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        var empresa = await ctx.EmpresaConfigService.ActualizarDatosAsync(
            token, "Smart Gym Centro", "5551234567", "Av. Reforma 100", "06600",
            razonSocial: "Centro Deportivo SA", rfc: "CDE120315ABC", regimenFiscal: "601");

        Assert.Equal("Smart Gym Centro", empresa.NombreComercial);
        Assert.Equal("CDE120315ABC", empresa.Rfc);

        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var fila = await conn.QuerySingleAsync<(string ValorAnterior, string ValorNuevo)>(new CommandDefinition(
            "SELECT valor_anterior, valor_nuevo FROM bitacora_auditoria WHERE accion = 'empresa.configuracion_editada'"));
        Assert.Contains("nombre:Smart Gym", fila.ValorAnterior);
        Assert.Contains("rfc:CDE120315ABC", fila.ValorNuevo);
    }

    [Fact]
    public async Task nombre_comercial_vacio_es_rechazado()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.EmpresaConfigService.ActualizarDatosAsync(
                token, "   ", null, null, null, null, null, null));
        Assert.Equal("nombre_comercial_obligatorio", ex.Code);
    }

    [Fact]
    public async Task guardar_y_quitar_logo()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var bytes = new byte[] { 137, 80, 78, 71 }; // firma PNG de prueba

        await ctx.EmpresaConfigService.GuardarLogoAsync(token, bytes, "image/png");
        Assert.NotNull(ctx.LogoStorage.LeerDataUrl());

        await ctx.EmpresaConfigService.QuitarLogoAsync(token);
        Assert.Null(ctx.LogoStorage.LeerDataUrl());

        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var acciones = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT accion FROM bitacora_auditoria WHERE accion LIKE 'empresa.logo%' ORDER BY created_at"));
        var lista = acciones.ToList();
        Assert.Contains("empresa.logo_actualizado", lista);
        Assert.Contains("empresa.logo_quitado", lista);
    }

    [Fact]
    public async Task impresora_se_guarda_y_sobrescribe()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        await ctx.EmpresaConfigService.GuardarImpresoraAsync(token, "HP LaserJet Recepcion");
        Assert.Equal("HP LaserJet Recepcion", await ctx.EmpresaConfigService.ObtenerImpresoraAsync(token));

        // Sobrescribe: la clave es unica.
        await ctx.EmpresaConfigService.GuardarImpresoraAsync(token, "Epson POS");
        Assert.Equal("Epson POS", await ctx.EmpresaConfigService.ObtenerImpresoraAsync(token));
    }

    [Fact]
    public async Task impresora_vacia_es_rechazada()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.EmpresaConfigService.GuardarImpresoraAsync(token, "   "));
        Assert.Equal("impresora_requerida", ex.Code);
    }

    [Fact]
    public async Task editar_configuracion_sin_permiso_falla()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        await Fase5Helper.ClearPermisosRolAsync(ctx);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.EmpresaConfigService.ObtenerAsync(token));
        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.Equal("sin_permiso", ex.Code);
    }

    [Fact]
    public async Task renombrar_sede_actualiza_y_registra_bitacora()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        var nombre = await ctx.EmpresaConfigService.RenombrarSedeAsync(token, "Sucursal Centro");

        Assert.Equal("Sucursal Centro", nombre);
        var sede = await ctx.Sedes.GetPrincipalAsync();
        Assert.NotNull(sede);
        Assert.Equal("Sucursal Centro", sede.Nombre);

        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var fila = await conn.QuerySingleAsync<(string TablaAfectada, string ValorAnterior, string ValorNuevo)>(new CommandDefinition(
            "SELECT tabla_afectada, valor_anterior, valor_nuevo FROM bitacora_auditoria WHERE accion = 'sede.renombrada'"));
        Assert.Equal("sedes", fila.TablaAfectada);
        Assert.Contains("Sede Principal", fila.ValorAnterior);
        Assert.Contains("Sucursal Centro", fila.ValorNuevo);
    }

    [Fact]
    public async Task renombrar_sede_vacia_es_rechazado()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.EmpresaConfigService.RenombrarSedeAsync(token, "   "));
        Assert.Equal("nombre_sede_obligatorio", ex.Code);
    }

    [Fact]
    public async Task renombrar_sede_sin_permiso_falla()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        await Fase5Helper.ClearPermisosRolAsync(ctx);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.EmpresaConfigService.RenombrarSedeAsync(token, "Otra Sede"));
        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.Equal("sin_permiso", ex.Code);
    }
}
