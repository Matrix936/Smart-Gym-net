using SmartGym.Core.Errors;
using SmartGym.Core.Services;

namespace SmartGym.Tests.Security;

/// <summary>Port de setup.rs (17 tests del checklist 03).</summary>
public sealed class SetupTests
{
    private static readonly byte[] LogoPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] LogoSvg = System.Text.Encoding.UTF8.GetBytes("<svg></svg>");
    private const string MimePng = "image/png";
    private const string MimeSvg = "image/svg+xml";

    private static SetupDatos Datos(byte[]? logo = null, string? mime = null, string? email = null, string? password = null, string? nombreComercial = null, string? nombreSede = null) =>
        new()
        {
            NombreComercial = nombreComercial ?? "Smart Gym",
            Telefono = "5555555555",
            Direccion = "Av. Principal 123",
            CodigoPostal = "01000",
            RazonSocial = null,
            Rfc = null,
            RegimenFiscal = null,
            NombreAdmin = null,
            Email = email ?? "admin@smartgym.test",
            Password = password ?? "password123",
            NombreSede = nombreSede,
            LogoBytes = logo,
            LogoMime = mime,
        };

    [Fact]
    public async Task verificar_estado_tabla_vacia_retorna_configuracion_pendiente()
    {
        using var ctx = new SecurityTestContext();
        var estado = await ctx.Setup.VerificarEstadoAsync();
        Assert.Equal(SetupEstadoResultado.Pendiente, estado.Estado);
        Assert.False(estado.Completado);
    }

    [Fact]
    public async Task verificar_estado_con_usuario_retorna_configuracion_completa()
    {
        using var ctx = new SecurityTestContext();
        await ctx.Setup.CompletarConfiguracionInicialAsync(Datos());

        var estado = await ctx.Setup.VerificarEstadoAsync();
        Assert.Equal(SetupEstadoResultado.Completa, estado.Estado);
        Assert.True(estado.Completado);
    }

    [Fact]
    public async Task completar_configuracion_inicial_exitoso()
    {
        using var ctx = new SecurityTestContext();

        await ctx.Setup.CompletarConfiguracionInicialAsync(Datos());

        var empresa = await ctx.Setup.ObtenerDatosEmpresaAsync();
        Assert.Equal("Smart Gym", empresa.NombreComercial);

        // El superadmin creado debe poder autenticarse.
        var login = await ctx.Auth.LoginAsync("admin@smartgym.test", "password123");
        Assert.False(string.IsNullOrWhiteSpace(login.Token));
    }

    [Fact]
    public async Task completar_configuracion_inicial_sin_datos_fiscales_exitoso()
    {
        using var ctx = new SecurityTestContext();
        await ctx.Setup.CompletarConfiguracionInicialAsync(Datos());

        var empresa = await ctx.Setup.ObtenerDatosEmpresaAsync();
        Assert.Null(empresa.RazonSocial);
        Assert.Null(empresa.Rfc);
        Assert.Null(empresa.RegimenFiscal);
    }

    [Fact]
    public async Task completar_configuracion_sin_nombre_sede_mantiene_sede_del_seed()
    {
        using var ctx = new SecurityTestContext();
        await ctx.Setup.CompletarConfiguracionInicialAsync(Datos());

        var sede = await ctx.Sedes.GetPrincipalAsync();
        Assert.NotNull(sede);
        Assert.Equal("Sede Principal", sede.Nombre);
    }

    [Fact]
    public async Task completar_configuracion_con_nombre_sede_renombra_sede_principal()
    {
        using var ctx = new SecurityTestContext();
        await ctx.Setup.CompletarConfiguracionInicialAsync(Datos(nombreSede: "Sucursal Centro"));

        var sede = await ctx.Sedes.GetPrincipalAsync();
        Assert.NotNull(sede);
        Assert.Equal("Sucursal Centro", sede.Nombre);

        // El superadmin sigue autenticando (el renombre no rompe el resto del setup).
        var login = await ctx.Auth.LoginAsync("admin@smartgym.test", "password123");
        Assert.False(string.IsNullOrWhiteSpace(login.Token));
    }

    [Fact]
    public async Task completar_configuracion_con_nombre_sede_vacio_mantiene_sede_del_seed()
    {
        using var ctx = new SecurityTestContext();
        await ctx.Setup.CompletarConfiguracionInicialAsync(Datos(nombreSede: "   "));

        var sede = await ctx.Sedes.GetPrincipalAsync();
        Assert.NotNull(sede);
        Assert.Equal("Sede Principal", sede.Nombre);
    }

    [Fact]
    public async Task completar_configuracion_password_corta_rechaza()
    {
        using var ctx = new SecurityTestContext();
        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Setup.CompletarConfiguracionInicialAsync(Datos(password: "1234567")));
        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("password_corta", ex.Code);
    }

    [Fact]
    public async Task completar_configuracion_email_invalido_rechaza()
    {
        using var ctx = new SecurityTestContext();
        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Setup.CompletarConfiguracionInicialAsync(Datos(email: "no-es-email")));
        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("email_invalido", ex.Code);
    }

    [Fact]
    public async Task completar_configuracion_nombre_comercial_vacio_rechaza()
    {
        using var ctx = new SecurityTestContext();
        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Setup.CompletarConfiguracionInicialAsync(Datos(nombreComercial: "  ")));
        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("nombre_comercial_vacio", ex.Code);
    }

    [Fact]
    public async Task completar_configuracion_rechaza_si_ya_existe_usuario()
    {
        using var ctx = new SecurityTestContext();
        await ctx.Setup.CompletarConfiguracionInicialAsync(Datos());

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Setup.CompletarConfiguracionInicialAsync(Datos(email: "otro@smartgym.test")));
        Assert.Equal(BusinessError.Conflict, ex.Error);
        Assert.Equal("setup_ya_completado", ex.Code);
    }

    [Fact]
    public async Task obtener_datos_empresa_con_empresa_configurada()
    {
        using var ctx = new SecurityTestContext();
        await ctx.Setup.CompletarConfiguracionInicialAsync(Datos());

        var empresa = await ctx.Setup.ObtenerDatosEmpresaAsync();
        Assert.Equal("Smart Gym", empresa.NombreComercial);
        Assert.Equal("5555555555", empresa.Telefono);
    }

    [Fact]
    public async Task obtener_datos_empresa_sin_empresa_retorna_not_found()
    {
        using var ctx = new SecurityTestContext();
        var ex = await Assert.ThrowsAsync<BusinessException>(() => ctx.Setup.ObtenerDatosEmpresaAsync());
        Assert.Equal(BusinessError.NotFound, ex.Error);
    }

    [Fact]
    public async Task obtener_datos_empresa_con_logo()
    {
        using var ctx = new SecurityTestContext();
        await ctx.Setup.CompletarConfiguracionInicialAsync(Datos());

        await ctx.Setup.GuardarLogoAsync(LogoPng, MimePng);

        var empresa = await ctx.Setup.ObtenerDatosEmpresaAsync();
        Assert.Equal("logos/logo.png", empresa.LogoPath);
    }

    [Fact]
    public async Task guardar_logo_mime_no_permitido_rechaza()
    {
        using var ctx = new SecurityTestContext();
        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Setup.GuardarLogoAsync(LogoPng, "application/pdf"));
        Assert.Equal(BusinessError.Validation, ex.Error);
    }

    [Fact]
    public async Task guardar_logo_tamanio_excesivo_rechaza()
    {
        using var ctx = new SecurityTestContext();
        var enorme = new byte[512 * 1024 + 1];
        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.Setup.GuardarLogoAsync(enorme, MimePng));
        Assert.Equal(BusinessError.Validation, ex.Error);
    }

    [Fact]
    public async Task guardar_logo_valido_png_guarda_archivo_deterministico()
    {
        using var ctx = new SecurityTestContext();
        await ctx.Setup.CompletarConfiguracionInicialAsync(Datos());

        var path = await ctx.Setup.GuardarLogoAsync(LogoPng, MimePng);

        Assert.Equal("logos/logo.png", path);
        Assert.True(File.Exists(Path.Combine(ctx.LogosDir, "logos", "logo.png")));
    }

    [Fact]
    public async Task guardar_logo_cambia_formato_elimina_huerfanos()
    {
        using var ctx = new SecurityTestContext();
        await ctx.Setup.CompletarConfiguracionInicialAsync(Datos());

        await ctx.Setup.GuardarLogoAsync(LogoPng, MimePng);
        Assert.True(File.Exists(Path.Combine(ctx.LogosDir, "logos", "logo.png")));

        var pathSvg = await ctx.Setup.GuardarLogoAsync(LogoSvg, MimeSvg);

        Assert.Equal("logos/logo.svg", pathSvg);
        Assert.False(File.Exists(Path.Combine(ctx.LogosDir, "logos", "logo.png")));
        Assert.True(File.Exists(Path.Combine(ctx.LogosDir, "logos", "logo.svg")));

        var empresa = await ctx.Setup.ObtenerDatosEmpresaAsync();
        Assert.Equal("logos/logo.svg", empresa.LogoPath);
    }

    [Fact]
    public async Task guardar_logo_mismo_formato_no_elimina()
    {
        using var ctx = new SecurityTestContext();
        await ctx.Setup.CompletarConfiguracionInicialAsync(Datos());

        await ctx.Setup.GuardarLogoAsync(LogoPng, MimePng);
        await ctx.Setup.GuardarLogoAsync([0x01, 0x02, 0x03], MimePng);

        var dir = Path.Combine(ctx.LogosDir, "logos");
        var archivos = Directory.GetFiles(dir, "logo.*");
        Assert.Single(archivos);
        Assert.EndsWith("logo.png", archivos[0]);
    }

    [Fact]
    public async Task completar_configuracion_con_logo_guarda_path_en_db()
    {
        using var ctx = new SecurityTestContext();

        await ctx.Setup.CompletarConfiguracionInicialAsync(Datos(logo: LogoPng, mime: MimePng));

        var empresa = await ctx.Setup.ObtenerDatosEmpresaAsync();
        Assert.Equal("logos/logo.png", empresa.LogoPath);
        Assert.True(File.Exists(Path.Combine(ctx.LogosDir, "logos", "logo.png")));
    }
}