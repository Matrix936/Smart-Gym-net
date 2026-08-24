using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase5;
using SmartGym.Tests.Fase6;
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
            token, "Smart Gym Centro",
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
                token, "   ", razonSocial: null, rfc: null, regimenFiscal: null));
        Assert.Equal("nombre_comercial_obligatorio", ex.Code);
    }

    [Fact]
    public async Task guardar_y_quitar_logo()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var bytes = new byte[] { 137, 80, 78, 71 };

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

    // ------------------------------------------------- kiosco estilo promos

    [Fact]
    public async Task estilo_promociones_kiosco_default_tarjetas_y_roundtrip_cinta()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        // Sin clave previa → default tarjetas (comportamiento actual).
        Assert.Equal(KioscoEstilosPromociones.Tarjetas,
            await ctx.EmpresaConfigService.ObtenerEstiloPromocionesKioscoAsync(token));

        await ctx.EmpresaConfigService.GuardarEstiloPromocionesKioscoAsync(token, KioscoEstilosPromociones.Cinta);
        Assert.Equal(KioscoEstilosPromociones.Cinta,
            await ctx.EmpresaConfigService.ObtenerEstiloPromocionesKioscoAsync(token));

        // Vuelve a tarjetas.
        await ctx.EmpresaConfigService.GuardarEstiloPromocionesKioscoAsync(token, KioscoEstilosPromociones.Tarjetas);
        Assert.Equal(KioscoEstilosPromociones.Tarjetas,
            await ctx.EmpresaConfigService.ObtenerEstiloPromocionesKioscoAsync(token));

        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var fila = await conn.QuerySingleAsync<(string Clave, string ValorNuevo)>(new CommandDefinition(
            "SELECT id_registro_afectado, valor_nuevo FROM bitacora_auditoria " +
            "WHERE accion = 'configuracion.estilo_promociones_kiosco_guardado' ORDER BY created_at DESC LIMIT 1"));
        Assert.Equal(KioscoEstilosPromociones.ClaveConfig, fila.Clave);
        Assert.Equal(KioscoEstilosPromociones.Tarjetas, fila.ValorNuevo);
    }

    [Fact]
    public async Task estilo_promociones_invalido_es_rechazado_y_desconocido_normaliza_a_tarjetas()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.EmpresaConfigService.GuardarEstiloPromocionesKioscoAsync(token, "marquesina"));
        Assert.Equal("estilo_promociones_invalido", ex.Code);

        // Valor crudo desconocido en BD → la lectura normaliza al default.
        await ctx.Configuracion.SetAsync(KioscoEstilosPromociones.ClaveConfig, "algo_raro");
        Assert.Equal(KioscoEstilosPromociones.Tarjetas,
            await ctx.EmpresaConfigService.ObtenerEstiloPromocionesKioscoAsync(token));
    }

    [Fact]
    public async Task guardar_estilo_promociones_sin_permiso_falla()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        await Fase5Helper.ClearPermisosRolAsync(ctx);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.EmpresaConfigService.GuardarEstiloPromocionesKioscoAsync(token, KioscoEstilosPromociones.Cinta));
        Assert.Equal(BusinessError.Unauthorized, ex.Error);
    }

    [Fact]
    public async Task marcar_incobrable_cambia_estado_y_registra_bitacora()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        var idPlan = await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = $"Plan-{UuidHelper.NewV4()[..8]}",
            DiasVigencia = 30,
            DiasCongelamientoMax = 0,
            PrecioCentavos = 10000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
        await ctx.MembresiasService.VenderAsync(token, idSocio, idPlan, "efectivo", 2000, sedeId);

        var idCuenta = await ctx.CuentasCobrar.BuscarAsync(sedeId, null, null, 1, 10).ContinueWith(
            t => t.Result.Items.First().IdCuenta);

        await ctx.CobranzaService.MarcarIncobrableAsync(token, idCuenta);

        var actualizada = await ctx.CuentasCobrar.GetByIdAsync(idCuenta);
        Assert.NotNull(actualizada);
        Assert.Equal(CuentaCobrarEstados.Incobrable, actualizada.Estado);
    }

    [Fact]
    public async Task marcar_incobrable_sobre_cuenta_inexistente_es_rechazado()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CobranzaService.MarcarIncobrableAsync(token, UuidHelper.NewV4()));
        Assert.Equal(BusinessError.NotFound, ex.Error);
    }
}
