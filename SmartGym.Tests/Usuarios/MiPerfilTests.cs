using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Usuarios;

/// <summary>Mi Perfil: edición de datos propios y cambio de contraseña.</summary>
public sealed class MiPerfilTests
{
    [Fact]
    public async Task editar_perfil_actualiza_datos_sesion_y_bitacora()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        var actualizada = await ctx.Auth.EditarPerfilAsync(
            token, "Jesús Daniel", "Aguilar", "González", "nuevo@smartgym.test");

        // La sesión en memoria refleja el cambio sin re-login.
        Assert.Equal("Jesús Daniel Aguilar González", actualizada.Nombre);
        Assert.Equal("nuevo@smartgym.test", actualizada.Email);
        Assert.Equal(ctx.SessionState.Current!.Email, actualizada.Email);

        // Persistido en DB.
        var usuario = (await ctx.Usuarios.GetByIdAsync(actualizada.IdUsuario))!;
        Assert.Equal("nuevo@smartgym.test", usuario.Email);

        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var n = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM bitacora_auditoria WHERE accion = 'usuario.perfil_editado'"));
        Assert.Equal(1, n);
    }

    [Fact]
    public async Task email_duplicado_de_otro_usuario_es_rechazado()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        await ctx.Usuarios.InsertAsync(new Usuario
        {
            Nombre = "Otro",
            Email = "ocupado@smartgym.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Fase4Helper.Password),
            IdRol = 1,
            EsActivo = true,
            CreatedAt = DateHelper.NowIsoUtc(),
            UpdatedAt = DateHelper.NowIsoUtc(),
        });

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Auth.EditarPerfilAsync(token, "X", null, null, "OCUPADO@smartgym.test"));
        Assert.Equal("email_en_uso", ex.Code);
    }

    [Fact]
    public async Task email_con_formato_invalido_es_rechazado()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Auth.EditarPerfilAsync(token, "X", null, null, "no-es-un-email"));
        Assert.Equal("email_invalido", ex.Code);
    }

    [Fact]
    public async Task cambiar_password_con_actual_correcta_permite_login_con_la_nueva()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        await ctx.Auth.CambiarPasswordAsync(token, Fase4Helper.Password, "nuevaClave123");

        // La vieja ya no sirve; la nueva sí.
        await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Auth.LoginAsync("admin@smartgym.test", Fase4Helper.Password));
        var login = await ctx.Auth.LoginAsync("admin@smartgym.test", "nuevaClave123");
        Assert.False(string.IsNullOrEmpty(login.Token));

        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var n = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM bitacora_auditoria WHERE accion = 'usuario.password_cambiado'"));
        Assert.Equal(1, n);
    }

    [Fact]
    public async Task cambiar_password_con_actual_incorrecta_falla()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Auth.CambiarPasswordAsync(token, "clave-erronea", "nuevaClave123"));
        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.Equal("clave_incorrecta", ex.Code);
    }

    [Fact]
    public async Task nueva_password_corta_es_rechazada()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();

        var exCorta = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Auth.CambiarPasswordAsync(token, Fase4Helper.Password, "corta12"));
        Assert.Equal("password_corta", exCorta.Code);

        // Misma regla que el SetupWizard: minimo 8 caracteres.
        await ctx.Auth.CambiarPasswordAsync(token, Fase4Helper.Password, "12345678");
    }
}
