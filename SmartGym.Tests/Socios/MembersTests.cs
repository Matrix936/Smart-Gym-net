using BCrypt.Net;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Services;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Socios;

/// <summary>Port de members.rs (13 tests del checklist 03).</summary>
public sealed class MembersTests
{
    private const string Password = "password123";

    private static CrearSocioDatos Datos(string nombre, string? email = null, string? telefono = null) => new()
    {
        Nombre = nombre,
        ApellidoPaterno = "García",
        Email = email,
        Telefono = telefono,
    };

    /// <summary>SUPERADMIN global (sin sede) con permisos y sesión válida.</summary>
    private static async Task<(SecurityTestContext ctx, string token, long sedeId)> SuperadminAsync()
    {
        var ctx = new SecurityTestContext();
        await ctx.Authz.SeedSuperadminPermisosAsync();
        await ctx.Setup.CompletarConfiguracionInicialAsync(new SetupDatos
        {
            NombreComercial = "Smart Gym",
            Telefono = "5555555555",
            Direccion = "Av. Principal 123",
            CodigoPostal = "01000",
            Email = "admin@smartgym.test",
            Password = Password,
        });
        var login = await ctx.Auth.LoginAsync("admin@smartgym.test", Password);
        var sede = (await ctx.Sedes.GetPrincipalAsync())!.IdSede;
        return (ctx, login.Token, sede);
    }

    /// <summary>Usuario con sede local (rol SUPERADMIN + sede) y permisos.</summary>
    private static async Task<(SecurityTestContext ctx, string token, long sedeId)> UsuarioConSedeAsync()
    {
        var ctx = new SecurityTestContext();
        await ctx.Authz.SeedSuperadminPermisosAsync();
        var rol = (await ctx.Roles.GetByNameAsync("SUPERADMIN"))!.IdRol;
        var sede = (await ctx.Sedes.GetPrincipalAsync())!;
        var now = DateHelper.NowIsoUtc();
        await ctx.Usuarios.InsertAsync(new Usuario
        {
            Nombre = "Local",
            ApellidoPaterno = "Sede",
            Email = "local@smartgym.test",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
            IdRol = rol,
            IdSede = sede.IdSede,
            EsActivo = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        var login = await ctx.Auth.LoginAsync("local@smartgym.test", Password);
        return (ctx, login.Token, sede.IdSede);
    }

    [Fact]
    public async Task create_member_genera_id_uuid_y_get_member_lo_recupera()
    {
        var (ctx, token, sedeId) = await SuperadminAsync();

        var socio = await ctx.SociosService.CrearSocioAsync(token, Datos("Manuel", email: "manuel@test.com"), sedeId);

        Assert.Equal(36, socio.IdSocio.Length);
        Assert.True(UuidHelper.IsValidV4(socio.IdSocio));
        Assert.Equal(SocioEstados.Activo, socio.Estado);

        var cargado = await ctx.SociosService.ObtenerSocioAsync(token, socio.IdSocio);
        Assert.Equal(socio.IdSocio, cargado.IdSocio);
        Assert.Equal("Manuel", cargado.Nombre);
    }

    [Fact]
    public async Task create_member_superadmin_sin_sede_usa_id_sede_del_frontend_valida_existencia()
    {
        var (ctx, token, sedeId) = await SuperadminAsync();

        var socio = await ctx.SociosService.CrearSocioAsync(token, Datos("Alicia"), sedeId);
        Assert.Equal(sedeId, socio.IdSedeRegistro);

        // Sede inexistente → validación falla.
        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.SociosService.CrearSocioAsync(token, Datos("Roberto"), 999999));
        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("sede_invalida", ex.Code);
    }

    [Fact]
    public async Task create_member_con_sesion_local_ignora_id_sede_del_frontend()
    {
        var (ctx, token, sedeId) = await UsuarioConSedeAsync();

        // El frontend envía una sede inexistente (999999): la sesión local manda.
        var socio = await ctx.SociosService.CrearSocioAsync(token, Datos("Carmen"), 999999);

        Assert.Equal(sedeId, socio.IdSedeRegistro);
    }

    [Fact]
    public async Task create_member_sin_sede_ni_frontend_da_error_validacion()
    {
        var (ctx, token, _) = await SuperadminAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.SociosService.CrearSocioAsync(token, Datos("Daniel")));
        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("sede_requerida", ex.Code);
    }

    [Fact]
    public async Task create_member_email_invalido_es_rechazado()
    {
        var (ctx, token, sedeId) = await SuperadminAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.SociosService.CrearSocioAsync(token, Datos("Elena", email: "no-es-email"), sedeId));
        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("email_invalido", ex.Code);
    }

    [Fact]
    public async Task create_member_nombre_vacio_es_rechazado()
    {
        var (ctx, token, sedeId) = await SuperadminAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.SociosService.CrearSocioAsync(token, Datos("   "), sedeId));
        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("nombre_vacio", ex.Code);
    }

    [Fact]
    public async Task search_members_encuentra_por_nombre_email_o_telefono()
    {
        var (ctx, token, sedeId) = await SuperadminAsync();
        var socio1 = await ctx.SociosService.CrearSocioAsync(token, Datos("Fernando", email: "fernando@test.com", telefono: "5551112233"), sedeId);
        var socio2 = await ctx.SociosService.CrearSocioAsync(token, Datos("Gabriela", email: "gabriela@test.com", telefono: "5554445566"), sedeId);

        Assert.Single((await ctx.SociosService.BuscarAsync(token, "Fernando")).Items);
        Assert.Single((await ctx.SociosService.BuscarAsync(token, "gabriela@test.com")).Items);
        Assert.Single((await ctx.SociosService.BuscarAsync(token, "5551112233")).Items);

        // Sin query → todos los activos.
        var todos = (await ctx.SociosService.BuscarAsync(token)).Items;
        Assert.Contains(todos, s => s.IdSocio == socio1.IdSocio);
        Assert.Contains(todos, s => s.IdSocio == socio2.IdSocio);
    }

    [Fact]
    public async Task update_member_actualiza_campos_seleccionados_y_preserva_id_sede_y_id()
    {
        var (ctx, token, sedeId) = await SuperadminAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Datos("Hugo", email: "hugo@test.com"), sedeId);

        var actualizado = await ctx.SociosService.ActualizarSocioAsync(token, new ActualizarSocioDatos
        {
            IdSocio = socio.IdSocio,
            Nombre = "Hugo Manuel",
            ApellidoPaterno = "Pérez",
            Telefono = "5550001122",
        });

        Assert.Equal(socio.IdSocio, actualizado.IdSocio);
        Assert.Equal(sedeId, actualizado.IdSedeRegistro);
        Assert.Equal("Hugo Manuel", actualizado.Nombre);
        Assert.Equal("5550001122", actualizado.Telefono);
    }

    [Fact]
    public async Task update_member_email_invalido_rechaza()
    {
        var (ctx, token, sedeId) = await SuperadminAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Datos("Iris"), sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.SociosService.ActualizarSocioAsync(token, new ActualizarSocioDatos
            {
                IdSocio = socio.IdSocio,
                Nombre = "Iris",
                Email = "correo-mal",
            }));
        Assert.Equal(BusinessError.Validation, ex.Error);
    }

    [Fact]
    public async Task cambiar_estado_socio_actualiza_estado_y_registra_historial()
    {
        var (ctx, token, sedeId) = await SuperadminAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Datos("Javier"), sedeId);

        await ctx.SociosService.CambiarEstadoAsync(token, socio.IdSocio, SocioEstados.Inactivo, "Terminó su contrato");

        var recargado = await ctx.SociosService.ObtenerSocioAsync(token, socio.IdSocio);
        Assert.Equal(SocioEstados.Inactivo, recargado.Estado);

        var historial = await ctx.Socios.HistorialDeAsync(socio.IdSocio);
        var fila = Assert.Single(historial);
        Assert.Equal(SocioEstados.Activo, fila.EstadoAnterior);
        Assert.Equal(SocioEstados.Inactivo, fila.EstadoNuevo);
        Assert.Equal("Terminó su contrato", fila.Motivo);
    }

    [Fact]
    public async Task cambiar_estado_socio_rechaza_estado_invalido()
    {
        var (ctx, token, sedeId) = await SuperadminAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Datos("Karla"), sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.SociosService.CambiarEstadoAsync(token, socio.IdSocio, "stado-no-existe"));
        Assert.Equal(BusinessError.Validation, ex.Error);
    }

    [Fact]
    public async Task soft_delete_marca_deleted_at_y_oculta_de_get_y_search()
    {
        var (ctx, token, sedeId) = await SuperadminAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Datos("Luis"), sedeId);

        await ctx.SociosService.EliminarSocioAsync(token, socio.IdSocio);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.SociosService.ObtenerSocioAsync(token, socio.IdSocio));
        Assert.Equal(BusinessError.NotFound, ex.Error);

        var busqueda = (await ctx.SociosService.BuscarAsync(token, "Luis")).Items;
        Assert.DoesNotContain(busqueda, s => s.IdSocio == socio.IdSocio);
    }

    [Fact]
    public async Task cambiar_estado_socio_es_atomico_falla_si_socio_inexistente()
    {
        var (ctx, token, _) = await SuperadminAsync();
        var idInexistente = UuidHelper.NewV4();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.SociosService.CambiarEstadoAsync(token, idInexistente, SocioEstados.Bloqueado));
        Assert.Equal(BusinessError.NotFound, ex.Error);

        // Nada quedó escrito: sin historial y sin bitácora del evento.
        Assert.Empty(await ctx.Socios.HistorialDeAsync(idInexistente));
        Assert.True(await ctx.Bitacora.NoExisteAccionParaAsync("socios", idInexistente));
    }

    [Fact]
    public async Task paginacion_respeta_tamano_pagina_y_reparte_correctamente_entre_paginas()
    {
        var (ctx, token, sedeId) = await SuperadminAsync();
        for (var i = 0; i < 12; i++)
        {
            await ctx.SociosService.CrearSocioAsync(token, Datos($"Paginado{i:D2}"), sedeId);
        }

        var pagina1 = await ctx.Socios.SearchAsync(null, pagina: 1, tamanoPagina: TamanosPagina.Diez);
        var pagina2 = await ctx.Socios.SearchAsync(null, pagina: 2, tamanoPagina: TamanosPagina.Diez);

        Assert.Equal(12, pagina1.TotalRegistros);
        Assert.Equal(2, pagina1.TotalPaginas);
        Assert.Equal(10, pagina1.Items.Count);
        Assert.Equal(2, pagina2.Items.Count);
        Assert.Empty(pagina1.Items.Select(s => s.IdSocio).Intersect(pagina2.Items.Select(s => s.IdSocio)));
    }

    [Fact]
    public async Task paginacion_conteo_total_respeta_el_filtro_de_busqueda_no_solo_la_pagina()
    {
        var (ctx, token, sedeId) = await SuperadminAsync();
        for (var i = 0; i < 5; i++)
        {
            await ctx.SociosService.CrearSocioAsync(token, Datos($"CoincideXyz{i}"), sedeId);
        }
        await ctx.SociosService.CrearSocioAsync(token, Datos("NoCoincide"), sedeId);

        var resultado = await ctx.Socios.SearchAsync("Xyz", pagina: 1, tamanoPagina: TamanosPagina.Diez);

        Assert.Equal(5, resultado.TotalRegistros);
        Assert.Equal(5, resultado.Items.Count);
        Assert.DoesNotContain(resultado.Items, s => s.Nombre == "NoCoincide");
    }

    [Fact]
    public async Task paginacion_pagina_fuera_de_rango_devuelve_vacio_sin_lanzar_error()
    {
        var (ctx, token, sedeId) = await SuperadminAsync();
        await ctx.SociosService.CrearSocioAsync(token, Datos("UnicoSocio"), sedeId);

        var resultado = await ctx.Socios.SearchAsync(null, pagina: 999, tamanoPagina: TamanosPagina.Diez);

        Assert.Empty(resultado.Items);
        Assert.Equal(1, resultado.TotalRegistros);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(100)]
    public async Task paginacion_tamano_pagina_invalido_lanza_argument_exception(int tamanoInvalido)
    {
        var (ctx, _, _) = await SuperadminAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => ctx.Socios.SearchAsync(null, pagina: 1, tamanoPagina: tamanoInvalido));
    }
}