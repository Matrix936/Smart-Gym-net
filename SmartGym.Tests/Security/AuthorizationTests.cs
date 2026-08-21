using BCrypt.Net;
using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;

namespace SmartGym.Tests.Security;

/// <summary>Port de authorization.rs (5 tests del checklist 03).</summary>
public sealed class AuthorizationTests
{
    private const string Password = "password123";

    private static async Task<long> CrearUsuario(SecurityTestContext ctx, string email, long idRol)
    {
        var now = DateHelper.NowIsoUtc();
        return await ctx.Usuarios.InsertAsync(new Usuario
        {
            Nombre = "Test",
            ApellidoPaterno = "Usuario",
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
            IdRol = idRol,
            EsActivo = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    [Fact]
    public async Task seed_inserta_todas_acciones_para_superadmin_cuando_tabla_vacia()
    {
        using var ctx = new SecurityTestContext();

        await ctx.Authz.SeedSuperadminPermisosAsync();

        var rol = await ctx.Roles.GetByNameAsync("SUPERADMIN");
        var permisos = await ctx.Permisos.GetByRolAsync(rol!.IdRol);
        var esperadas = PermisoCatalogo.Todas();
        Assert.Equal(esperadas.Count, permisos.Count);
        foreach (var accion in esperadas)
        {
            Assert.Contains(permisos, p => p.Accion == accion);
        }
    }

    [Fact]
    public async Task seed_es_idempotente_no_duplica_si_ya_poblada()
    {
        using var ctx = new SecurityTestContext();

        await ctx.Authz.SeedSuperadminPermisosAsync();
        await ctx.Authz.SeedSuperadminPermisosAsync();

        var rol = await ctx.Roles.GetByNameAsync("SUPERADMIN");
        var permisos = await ctx.Permisos.GetByRolAsync(rol!.IdRol);
        Assert.Equal(PermisoCatalogo.Todas().Count, permisos.Count);
    }

    [Fact]
    public async Task seed_habilita_cobranza_registrar_abono_para_superadmin()
    {
        using var ctx = new SecurityTestContext();
        await ctx.Authz.SeedSuperadminPermisosAsync();

        var rolSa = await ctx.Roles.GetByNameAsync("SUPERADMIN");
        await CrearUsuario(ctx, "cobranza-sa@smartgym.test", rolSa!.IdRol);
        var login = await ctx.Auth.LoginAsync("cobranza-sa@smartgym.test", Password);

        await ctx.Authz.RequierePermisoAsync(login.Token, PermisoCatalogo.CobranzaRegistrarAbono); // no lanza
    }

    [Fact]
    public async Task requiere_permiso_llama_a_una_accion_no_presente_es_denegada()
    {
        using var ctx = new SecurityTestContext();
        await ctx.Authz.SeedSuperadminPermisosAsync();

        var rolVendedor = await ctx.Roles.InsertAsync(new Rol
        {
            Nombre = "CAJERO",
            Descripcion = "Sin permiso de cobranza",
            CreatedAt = DateHelper.NowIsoUtc(),
        });
        await CrearUsuario(ctx, "cajero@smartgym.test", rolVendedor);
        var login = await ctx.Auth.LoginAsync("cajero@smartgym.test", Password);

        // "cobranza.registrar_abono" NO está en el catálogo del rol CAJERO → denegada.
        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Authz.RequierePermisoAsync(login.Token, PermisoCatalogo.CobranzaRegistrarAbono));

        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.Equal("sin_permiso", ex.Code);
    }

    [Fact]
    public async Task seed_sincroniza_acciones_faltantes_en_base_ya_sembrada()
    {
        using var ctx = new SecurityTestContext();
        var rol = await ctx.Roles.GetByNameAsync("SUPERADMIN");

        // Simula una BD sembrada por una versión anterior con catálogo más chico:
        // solo dos acciones presentes, la mayoría faltantes.
        await ctx.Permisos.ReplaceAccionesForRolAsync(rol!.IdRol, new[] { "caja.abrir", "pos.vender" });

        await ctx.Authz.SeedSuperadminPermisosAsync();

        // El seed incremental completa el catálogo sin duplicar lo existente.
        var permisos = await ctx.Permisos.GetByRolAsync(rol.IdRol);
        Assert.Equal(PermisoCatalogo.Todas().Count, permisos.Count);
        foreach (var accion in PermisoCatalogo.Todas())
        {
            Assert.Contains(permisos, p => p.Accion == accion);
        }
    }
}