using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Accesos;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Fase5;

/// <summary>Port de access.rs (15 tests del checklist 03).</summary>
public sealed class AccessTests
{
    [Fact]
    public async Task kiosko_concedido_membresia_activa()
    {
        var (ctx, _token, sedeId, idSocio, _planId, idDispositivo) = await Fase5Helper.BaseAccessAsync();

        var res = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId, idDispositivo);

        Assert.Equal(AccesoEstados.Concedido, res.Estado);
        Assert.Null(res.MotivoDenegacion);
        Assert.NotNull(res.Socio);
        Assert.Equal(idSocio, res.Socio!.IdSocio);
        Assert.Equal("Juan", res.Socio.Nombre);
    }

    [Fact]
    public async Task kiosko_concedido_alternancia_tipo()
    {
        var (ctx, _token, sedeId, idSocio, _planId, idDispositivo) = await Fase5Helper.BaseAccessAsync();

        var r1 = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId, idDispositivo);
        // Ventana anti-doble-toque: retrocede el timestamp del primero para
        // simular que el segundo toque ocurrió después de la ventana.
        ModoRegistroAccesoTests.RetrocederTimestampAsync(ctx, r1.IdAcceso, minutos: 5);
        var r2 = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId, idDispositivo);

        var bitacora1 = await ctx.Accesos.GetByIdAsync(r1.IdAcceso);
        var bitacora2 = await ctx.Accesos.GetByIdAsync(r2.IdAcceso);
        Assert.Equal(AccesoTipos.Entrada, bitacora1!.Tipo);
        Assert.Equal(AccesoTipos.Salida, bitacora2!.Tipo);
    }

    [Fact]
    public async Task kiosko_denegado_membresia_vencida()
    {
        var (ctx, _token, sedeId, idSocio, _planId, idDispositivo) = await Fase5Helper.BaseAccessAsync();
        await Fase5Helper.SetMembresiaEstadoAsync(ctx, idSocio, MembresiaEstados.Vencida, "2020-01-01");

        var res = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId, idDispositivo);

        Assert.Equal(AccesoEstados.Denegado, res.Estado);
        Assert.Equal("membresia_vencida", res.MotivoDenegacion);
        Assert.Null(res.Socio);
    }

    [Fact]
    public async Task kiosko_denegado_membresia_congelada()
    {
        var (ctx, _token, sedeId, idSocio, _planId, idDispositivo) = await Fase5Helper.BaseAccessAsync();
        await Fase5Helper.SetMembresiaEstadoAsync(ctx, idSocio, MembresiaEstados.Congelada);

        var res = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId, idDispositivo);

        Assert.Equal(AccesoEstados.Denegado, res.Estado);
        Assert.Equal("membresia_congelada", res.MotivoDenegacion);
        Assert.Null(res.Socio);
    }

    [Fact]
    public async Task kiosko_socio_bloqueado_denegado()
    {
        var (ctx, token, sedeId, idSocio, _planId, idDispositivo) = await Fase5Helper.BaseAccessAsync();
        await ctx.SociosService.CambiarEstadoAsync(token, idSocio, SocioEstados.Bloqueado, "prueba");

        var res = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId, idDispositivo);

        Assert.Equal(AccesoEstados.Denegado, res.Estado);
        Assert.Equal("socio_bloqueado", res.MotivoDenegacion);
        Assert.Null(res.Socio);
    }

    [Fact]
    public async Task kiosko_socio_inactivo_denegado()
    {
        var (ctx, token, sedeId, idSocio, _planId, idDispositivo) = await Fase5Helper.BaseAccessAsync();
        await ctx.SociosService.CambiarEstadoAsync(token, idSocio, SocioEstados.Inactivo, "prueba");

        var res = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId, idDispositivo);

        Assert.Equal(AccesoEstados.Denegado, res.Estado);
        Assert.Equal("socio_inactivo", res.MotivoDenegacion);
        Assert.Null(res.Socio);
    }

    [Fact]
    public async Task kiosko_socio_suspendido_denegado()
    {
        var (ctx, token, sedeId, idSocio, _planId, idDispositivo) = await Fase5Helper.BaseAccessAsync();
        await ctx.SociosService.CambiarEstadoAsync(token, idSocio, SocioEstados.Suspendido, "prueba");

        var res = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId, idDispositivo);

        Assert.Equal(AccesoEstados.Denegado, res.Estado);
        Assert.Equal(MotivosDenegacionAcceso.SocioSuspendido, res.MotivoDenegacion);
        Assert.Null(res.Socio);
    }

    [Fact]
    public async Task kiosko_socio_inexistente()
    {
        var (ctx, _token, sedeId, _idSocio, _planId, idDispositivo) = await Fase5Helper.BaseAccessAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.AccesoService.RegistrarAccesoKioskoAsync(UuidHelper.NewV4(), sedeId, idDispositivo));

        Assert.Equal(BusinessError.NotFound, ex.Error);
        Assert.Equal("socio_no_encontrado", ex.Code);
    }

    [Fact]
    public async Task kiosko_dispositivo_invalido()
    {
        var (ctx, _token, sedeId, idSocio, _planId, _idDispositivo) = await Fase5Helper.BaseAccessAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId, 999999));

        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("dispositivo_invalido", ex.Code);
        Assert.Contains("dispositivo", ex.Message);
    }

    [Fact]
    public async Task kiosko_dispositivo_none_es_null()
    {
        var (ctx, _token, sedeId, idSocio, _planId, _idDispositivo) = await Fase5Helper.BaseAccessAsync();

        var res = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId, null);

        Assert.Equal(AccesoEstados.Concedido, res.Estado);
        var bitacora = await ctx.Accesos.GetByIdAsync(res.IdAcceso);
        Assert.Null(bitacora!.IdDispositivo);
    }

    [Fact]
    public async Task manual_concedido_membresia_activa()
    {
        var (ctx, token, sedeId, idSocio, _planId, idDispositivo) = await Fase5Helper.BaseAccessAsync();

        var res = await ctx.AccesoService.RegistrarAccesoManualAsync(token, idSocio, sedeId, idDispositivo);

        Assert.Equal(AccesoEstados.Concedido, res.Estado);
        var bitacora = await ctx.Accesos.GetByIdAsync(res.IdAcceso);
        Assert.Equal(AccesoMetodos.Manual, bitacora!.Metodo);
    }

    [Fact]
    public async Task manual_denegado_membresia_vencida()
    {
        var (ctx, token, sedeId, idSocio, _planId, idDispositivo) = await Fase5Helper.BaseAccessAsync();
        await Fase5Helper.SetMembresiaEstadoAsync(ctx, idSocio, MembresiaEstados.Vencida, "2020-01-01");

        var res = await ctx.AccesoService.RegistrarAccesoManualAsync(token, idSocio, sedeId, idDispositivo);

        Assert.Equal(AccesoEstados.Denegado, res.Estado);
        Assert.Equal("membresia_vencida", res.MotivoDenegacion);
        var bitacora = await ctx.Accesos.GetByIdAsync(res.IdAcceso);
        Assert.Equal(AccesoMetodos.Manual, bitacora!.Metodo);
    }

    [Fact]
    public async Task manual_sin_sesion_falla()
    {
        var (ctx, _token, sedeId, idSocio, _planId, idDispositivo) = await Fase5Helper.BaseAccessAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.AccesoService.RegistrarAccesoManualAsync("token-invalido", idSocio, sedeId, idDispositivo));

        Assert.Equal(BusinessError.Unauthorized, ex.Error);
    }

    [Fact]
    public async Task manual_sin_permiso_falla()
    {
        var (ctx, token, sedeId, idSocio, _planId, idDispositivo) = await Fase5Helper.BaseAccessAsync();
        await Fase5Helper.ClearPermisosRolAsync(ctx);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.AccesoService.RegistrarAccesoManualAsync(token, idSocio, sedeId, idDispositivo));

        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.Equal("sin_permiso", ex.Code);
    }

    [Fact]
    public async Task fecha_ultimo_acceso_actualizada_solo_concedido()
    {
        var (ctx, _token, sedeId, idSocio, _planId, idDispositivo) = await Fase5Helper.BaseAccessAsync();

        var antes = (await ctx.Socios.GetByIdAsync(idSocio))!.FechaUltimoAcceso;
        Assert.Null(antes);

        await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId, idDispositivo);
        var despuesConcedido = (await ctx.Socios.GetByIdAsync(idSocio))!.FechaUltimoAcceso;
        Assert.NotNull(despuesConcedido);

        await Fase5Helper.SetMembresiaEstadoAsync(ctx, idSocio, MembresiaEstados.Vencida);
        await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId, idDispositivo);
        var despuesDenegado = (await ctx.Socios.GetByIdAsync(idSocio))!.FechaUltimoAcceso;
        Assert.Equal(despuesConcedido, despuesDenegado);
    }

    [Fact]
    public async Task kiosko_prioriza_membresia_activa_sobre_cancelada_y_vencidas()
    {
        var (ctx, token, sedeId, idSocio, planId, idDispositivo) = await Fase5Helper.BaseAccessAsync();

        await Fase5Helper.InsertarMembresiaAsync(
            ctx, idSocio, planId, sedeId, MembresiaEstados.Cancelada,
            "2025-01-01T00:00:00.000Z", "2027-12-31T23:59:59.999Z");

        for (var i = 1; i <= 3; i++)
        {
            await Fase5Helper.InsertarMembresiaAsync(
                ctx, idSocio, planId, sedeId, MembresiaEstados.Vencida,
                $"2024-{i:00}-01T00:00:00.000Z", $"2024-{i:00}-28T23:59:59.999Z");
        }

        var res = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId, idDispositivo);

        Assert.Equal(AccesoEstados.Concedido, res.Estado);
        Assert.NotNull(res.Socio);
    }
}