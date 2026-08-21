using BCrypt.Net;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Services;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Fase4;

/// <summary>Helpers compartidos por los tests de caja y membresías (Fase 4).</summary>
internal static class Fase4Helper
{
    public const string Password = "password123";
    public const string MetodoPago = "efectivo";

    public static async Task<SecurityTestContext> BaseAsync()
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
        return ctx;
    }

    public static async Task<(string token, long sedeId)> LoginSuperadminAsync(SecurityTestContext ctx)
    {
        var login = await ctx.Auth.LoginAsync("admin@smartgym.test", Password);
        var sede = (await ctx.Sedes.GetPrincipalAsync())!.IdSede;
        return (login.Token, sede);
    }

    public static async Task<(SecurityTestContext ctx, string token, long sedeId)> SuperadminAsync()
    {
        var ctx = await BaseAsync();
        var (token, sedeId) = await LoginSuperadminAsync(ctx);
        return (ctx, token, sedeId);
    }

    /// <summary>Usuario con id_sede (rol SUPERADMIN) — la sesión local manda sobre el frontend.</summary>
    public static async Task<(string token, long sedeId)> LoginLocalAsync(SecurityTestContext ctx)
    {
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
        return (login.Token, sede.IdSede);
    }

    public static async Task<long> CrearPlanAsync(
        SecurityTestContext ctx,
        int diasVigencia,
        int diasCongelamientoMax,
        long precioCentavos)
    {
        return await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = $"Plan {diasVigencia}d ${precioCentavos}",
            DiasVigencia = diasVigencia,
            DiasCongelamientoMax = diasCongelamientoMax,
            PrecioCentavos = precioCentavos,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
    }

    public static CrearSocioDatos DatosSocio(string nombre) => new()
    {
        Nombre = nombre,
        ApellidoPaterno = "García",
    };

    /// <summary>Sede inactiva (es_activa=false) — para probar el rechazo unificado de ISedeResolutionService.</summary>
    public static Task<long> InsertarSedeInactivaAsync(SecurityTestContext ctx) =>
        ctx.Sedes.InsertAsync(new Sede
        {
            Nombre = "Sede Inactiva",
            EsActiva = false,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });

    /// <summary>Inserta un movimiento de caja directo (útil para montos mixtos del cierre).</summary>
    public static async Task InsertarMovimientoAsync(
        SecurityTestContext ctx,
        string idSesion,
        string tipo,
        long montoCentavos,
        bool afectaEfectivo)
    {
        var now = DateHelper.NowIsoUtc();
        await ctx.Movimientos.InsertAsync(new CajaMovimiento
        {
            IdMovimiento = UuidHelper.NewV4(),
            IdSesion = idSesion,
            Tipo = tipo,
            MontoCentavos = montoCentavos,
            MetodoPago = MetodoPago,
            AfectaEfectivo = afectaEfectivo,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }
}