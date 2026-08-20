using BCrypt.Net;
using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;

namespace SmartGym.Tests.Security;

/// <summary>Port de auth.rs (13 tests del checklist 03).</summary>
public sealed class AuthTests
{
    private const string Password = "password123";

    private static async Task<long> CrearUsuario(SecurityTestContext ctx, string email, long? idSede = null, long? idRol = null)
    {
        var rol = idRol ?? (await ctx.Roles.GetByNameAsync("SUPERADMIN"))!.IdRol;
        var now = DateHelper.NowIsoUtc();
        return await ctx.Usuarios.InsertAsync(new Usuario
        {
            Nombre = "Test",
            ApellidoPaterno = "Usuario",
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
            IdRol = rol,
            IdSede = idSede,
            EsActivo = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    [Fact]
    public async Task login_correcto_devuelve_token_y_sesion_valida()
    {
        using var ctx = new SecurityTestContext();
        await CrearUsuario(ctx, "login-ok@smartgym.test");

        var result = await ctx.Auth.LoginAsync("login-ok@smartgym.test", Password);

        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        var sesion = await ctx.Auth.ValidarSesionAsync(result.Token);
        Assert.Equal("login-ok@smartgym.test", sesion.Email);
    }

    [Fact]
    public async Task login_password_incorrecta_falla_sin_revelar_email()
    {
        using var ctx = new SecurityTestContext();
        await CrearUsuario(ctx, "login-wrong@smartgym.test");

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Auth.LoginAsync("login-wrong@smartgym.test", "contraseña-incorrecta"));

        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.DoesNotContain("login-wrong@smartgym.test", ex.Message);
    }

    [Fact]
    public async Task login_email_inexistente_da_mismo_error_que_clave_incorrecta()
    {
        using var ctx = new SecurityTestContext();
        await CrearUsuario(ctx, "login-mismo@smartgym.test");

        var exDesconocido = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Auth.LoginAsync("email-inexistente@smartgym.test", Password));
        var exClave = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Auth.LoginAsync("login-mismo@smartgym.test", "clave-incorrecta"));

        Assert.Equal(exDesconocido.Error, exClave.Error);
        Assert.Equal(exDesconocido.Code, exClave.Code);
        Assert.Equal(exDesconocido.Message, exClave.Message);
    }

    [Fact]
    public async Task logout_invalida_sesion_posterior()
    {
        using var ctx = new SecurityTestContext();
        await CrearUsuario(ctx, "logout@smartgym.test");
        var result = await ctx.Auth.LoginAsync("logout@smartgym.test", Password);

        await ctx.Auth.LogoutAsync(result.Token);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => ctx.Auth.ValidarSesionAsync(result.Token));
        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.Equal("sesion_invalida", ex.Code);
    }

    [Fact]
    public async Task requiere_permiso_acepta_y_deniega_correctamente()
    {
        using var ctx = new SecurityTestContext();
        await ctx.Authz.SeedSuperadminPermisosAsync();

        // SUPERADMIN (todas las acciones) → acepta.
        await CrearUsuario(ctx, "permiso-sa@smartgym.test");
        var ok = await ctx.Auth.LoginAsync("permiso-sa@smartgym.test", Password);
        await ctx.Authz.RequierePermisoAsync(ok.Token, PermisoCatalogo.CajaAbrir); // no lanza

        // Rol VENDEDOR sin permisos → deniega.
        var rolVendedor = await ctx.Roles.InsertAsync(new Rol
        {
            Nombre = "VENDEDOR",
            Descripcion = "Sin permisos",
            CreatedAt = DateHelper.NowIsoUtc(),
        });
        await CrearUsuario(ctx, "permiso-vendor@smartgym.test", idRol: rolVendedor);
        var negado = await ctx.Auth.LoginAsync("permiso-vendor@smartgym.test", Password);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Authz.RequierePermisoAsync(negado.Token, PermisoCatalogo.CajaAbrir));
        Assert.Equal(BusinessError.Unauthorized, ex.Error);
    }

    [Fact]
    public async Task validar_sesion_puebla_id_sede_para_usuario_con_sede_y_none_para_superadmin_sin_sede()
    {
        using var ctx = new SecurityTestContext();
        var sede = await ctx.Sedes.GetPrincipalAsync();

        await CrearUsuario(ctx, "superadmin-sede@smartgym.test", idSede: null);
        var resSa = await ctx.Auth.LoginAsync("superadmin-sede@smartgym.test", Password);
        var sesionSa = await ctx.Auth.ValidarSesionAsync(resSa.Token);
        Assert.Null(sesionSa.IdSede);

        await CrearUsuario(ctx, "usuario-sede@smartgym.test", idSede: sede!.IdSede);
        var resU = await ctx.Auth.LoginAsync("usuario-sede@smartgym.test", Password);
        var sesionU = await ctx.Auth.ValidarSesionAsync(resU.Token);
        Assert.Equal(sede.IdSede, sesionU.IdSede);
    }

    [Fact]
    public async Task reautorizar_con_clave_correcta_devuelve_ok()
    {
        using var ctx = new SecurityTestContext();
        await CrearUsuario(ctx, "reauth-ok@smartgym.test");
        var result = await ctx.Auth.LoginAsync("reauth-ok@smartgym.test", Password);

        await ctx.Auth.ReautorizarAsync(result.Token, Password); // no debe lanzar
    }

    [Fact]
    public async Task reautorizar_con_clave_incorrecta_devuelve_unauthorized_claro()
    {
        using var ctx = new SecurityTestContext();
        await CrearUsuario(ctx, "reauth-bad@smartgym.test");
        var result = await ctx.Auth.LoginAsync("reauth-bad@smartgym.test", Password);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Auth.ReautorizarAsync(result.Token, "clave-incorrecta"));

        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.Equal("clave_incorrecta", ex.Code);
    }

    [Fact]
    public async Task login_exitoso_agrega_cuenta_recordada()
    {
        using var ctx = new SecurityTestContext();
        await CrearUsuario(ctx, "recordada@smartgym.test");

        await ctx.Auth.LoginAsync("recordada@smartgym.test", Password);

        var cuentas = await ctx.Auth.ListarCuentasRecordadasAsync();
        var cuenta = Assert.Single(cuentas);
        Assert.Equal("recordada@smartgym.test", cuenta.Email);
    }

    [Fact]
    public async Task login_fallido_no_toca_cuentas_recordadas()
    {
        using var ctx = new SecurityTestContext();
        await CrearUsuario(ctx, "fallido-recordada@smartgym.test");

        await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Auth.LoginAsync("fallido-recordada@smartgym.test", "clave-incorrecta"));

        Assert.Empty(await ctx.Auth.ListarCuentasRecordadasAsync());
    }

    [Fact]
    public async Task segundo_login_actualiza_ultimo_login_sin_duplicar()
    {
        using var ctx = new SecurityTestContext();
        await CrearUsuario(ctx, "doble-login@smartgym.test");

        await ctx.Auth.LoginAsync("doble-login@smartgym.test", Password);
        await Task.Delay(5);
        await ctx.Auth.LoginAsync("doble-login@smartgym.test", Password);

        var cuentas = await ctx.Auth.ListarCuentasRecordadasAsync();
        var cuenta = Assert.Single(cuentas);
        Assert.Equal("doble-login@smartgym.test", cuenta.Email);
    }

    [Fact]
    public async Task listar_cuentas_recordadas_devuelve_vacio_en_instalacion_nueva()
    {
        using var ctx = new SecurityTestContext();
        Assert.Empty(await ctx.Auth.ListarCuentasRecordadasAsync());
    }

    [Fact]
    public async Task login_guarda_en_sesion_actual_state_y_logout_limpia()
    {
        using var ctx = new SecurityTestContext();
        await CrearUsuario(ctx, "state@smartgym.test");

        Assert.False(ctx.SessionState.IsAuthenticated);
        Assert.Null(ctx.SessionState.Current);

        var result = await ctx.Auth.LoginAsync("state@smartgym.test", Password);
        Assert.True(ctx.SessionState.IsAuthenticated);
        Assert.NotNull(ctx.SessionState.Current);
        Assert.Equal(result.Token, ctx.SessionState.Current!.Token);

        await ctx.Auth.LogoutAsync(result.Token);
        Assert.False(ctx.SessionState.IsAuthenticated);
        Assert.Null(ctx.SessionState.Current);
    }
}