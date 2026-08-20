using Microsoft.Data.Sqlite;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Data.Repositories;

namespace SmartGym.Tests.Data;

/// <summary>Round-trips de la capa de datos (Fase 1) sobre la BD temporal.</summary>
[Collection("data")]
public sealed class RepositoriesTests
{
    private readonly DataTestFixture _fixture;

    public RepositoriesTests(DataTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Sedes_lee_el_seed_sede_principal()
    {
        var repo = new SedesRepository(_fixture.DbPath);

        var sede = await repo.GetPrincipalAsync();

        Assert.NotNull(sede);
        Assert.Equal("Sede Principal", sede!.Nombre);
        Assert.True(sede.EsActiva);
    }

    [Fact]
    public async Task Roles_lee_el_seed_SUPERADMIN()
    {
        var repo = new RolesRepository(_fixture.DbPath);

        var rol = await repo.GetByNameAsync("SUPERADMIN");

        Assert.NotNull(rol);
        Assert.Equal("SUPERADMIN", rol!.Nombre);
        Assert.True(rol.IdRol > 0);
    }

    [Fact]
    public async Task Permisos_rol_reemplazo_atomico_por_rol()
    {
        var repo = new PermisosRolRepository(_fixture.DbPath);
        var rolesRepo = new RolesRepository(_fixture.DbPath);
        var rol = await rolesRepo.GetByNameAsync("SUPERADMIN");

        await repo.ReplaceAccionesForRolAsync(rol!.IdRol, new[] { "acceso.registrar", "venta.crear", "caja.abrir" });

        var permisos = await repo.GetByRolAsync(rol.IdRol);
        Assert.Equal(3, permisos.Count);
        Assert.Contains(permisos, p => p.Accion == "acceso.registrar");

        // Reemplazo con lista menor: no debe quedar residuo de la anterior.
        await repo.ReplaceAccionesForRolAsync(rol.IdRol, new[] { "venta.crear" });
        var after = await repo.GetByRolAsync(rol.IdRol);
        Assert.Single(after);
        Assert.Equal("venta.crear", after[0].Accion);
    }

    [Fact]
    public async Task Usuarios_round_trip_y_email_unico()
    {
        var repo = new UsuariosRepository(_fixture.DbPath);
        var rolesRepo = new RolesRepository(_fixture.DbPath);
        var rol = await rolesRepo.GetByNameAsync("SUPERADMIN");
        var sedeRepo = new SedesRepository(_fixture.DbPath);
        var sede = await sedeRepo.GetPrincipalAsync();

        var now = DateHelper.NowIsoUtc();
        var usuario = new Usuario
        {
            Nombre = "Admin",
            ApellidoPaterno = "Sistema",
            Email = "admin-fase1@smartgym.test",
            PasswordHash = "$2a$hash-prueba",
            IdRol = rol!.IdRol,
            IdSede = sede!.IdSede,
            EsActivo = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var id = await repo.InsertAsync(usuario);
        Assert.True(id > 0);

        var cargado = await repo.GetByIdAsync(id);
        Assert.NotNull(cargado);
        Assert.Equal("admin-fase1@smartgym.test", cargado!.Email);
        Assert.Equal(rol.IdRol, cargado.IdRol);
        Assert.True(cargado.EsActivo);

        var porEmail = await repo.GetByEmailAsync("ADMIN-FASE1@smartgym.test");
        Assert.NotNull(porEmail);
        Assert.Equal(id, porEmail!.IdUsuario);

        var duplicado = new Usuario
        {
            Nombre = "Otro",
            Email = "admin-fase1@smartgym.test",
            PasswordHash = "x",
            IdRol = rol.IdRol,
            EsActivo = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await Assert.ThrowsAsync<SqliteException>(() => repo.InsertAsync(duplicado));
    }

    [Fact]
    public async Task Configuracion_set_y_get_round_trip()
    {
        var repo = new ConfiguracionRepository(_fixture.DbPath);

        Assert.Null(await repo.GetAsync("setup.completado"));

        await repo.SetAsync("setup.completado", "true");
        Assert.Equal("true", await repo.GetAsync("setup.completado"));

        await repo.SetAsync("setup.completado", "false");
        Assert.Equal("false", await repo.GetAsync("setup.completado"));
    }

    [Fact]
    public async Task Uuid_helper_genera_formato_estandar()
    {
        var id = UuidHelper.NewV4();
        Assert.Equal(36, id.Length);
        Assert.True(UuidHelper.IsValidV4(id));
        Assert.Matches("^[0-9a-f-]{36}$", id);
    }
}