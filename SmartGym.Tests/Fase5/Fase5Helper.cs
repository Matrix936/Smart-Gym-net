using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Fase5;

/// <summary>Helpers compartidos por access.rs y biometrics.rs portados (15 + 20 tests).</summary>
internal static class Fase5Helper
{
    public const long Precio = 10000;

    /// <summary>Escenario base para access.rs: superadmin, plan 30d/7cong, socio activo, dispositivo y membresía activa vendida.</summary>
    public static async Task<(SecurityTestContext ctx, string token, long sedeId, string idSocio, long planId, long idDispositivo)> BaseAccessAsync()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var planId = await Fase4Helper.CrearPlanAsync(ctx, 30, 7, Precio);
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Juan"), sedeId);
        var idDispositivo = await InsertarDispositivoAsync(ctx, sedeId, "Lector Principal");
        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);
        await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, Fase4Helper.MetodoPago, Precio, sedeId);
        return (ctx, token, sedeId, socio.IdSocio, planId, idDispositivo);
    }

    public static Task<long> InsertarDispositivoAsync(SecurityTestContext ctx, long idSede, string nombre) =>
        ctx.DispositivosAcceso.InsertAsync(new DispositivoAcceso
        {
            Nombre = nombre,
            Tipo = DispositivoAccesoTipos.Biometrico,
            IdSede = idSede,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });

    /// <summary>Cambia el estado de la membreía del socio (vencida/congelada, opcional fecha_fin).</summary>
    public static async Task SetMembresiaEstadoAsync(
        SecurityTestContext ctx,
        string idSocio,
        string estado,
        string? fechaFin = null)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var sql = fechaFin is null
            ? "UPDATE membresias SET estado = @estado WHERE id_socio = @idSocio AND deleted_at IS NULL"
            : "UPDATE membresias SET estado = @estado, fecha_fin = @fechaFin WHERE id_socio = @idSocio AND deleted_at IS NULL";
        await conn.ExecuteAsync(new CommandDefinition(sql, new { estado, fechaFin, idSocio }));
    }

    /// <summary>INSERT directo de membresía con estado/fechas arbitrarios (escenarios multi/mixtos).</summary>
    public static async Task InsertarMembresiaAsync(
        SecurityTestContext ctx,
        string idSocio,
        long idPlan,
        long idSede,
        string estado,
        string fechaInicio,
        string fechaFin)
    {
        var ahora = DateHelper.NowIsoUtc();
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO membresias (id_membresia, id_socio, id_plan, id_sede, fecha_inicio, fecha_fin, " +
            "estado, created_at, updated_at, sincronizado) " +
            "VALUES (@IdMembresia, @IdSocio, @IdPlan, @IdSede, @FechaInicio, @FechaFin, " +
            "@Estado, @CreatedAt, @UpdatedAt, 0);",
            new
            {
                IdMembresia = UuidHelper.NewV4(),
                IdSocio = idSocio,
                IdPlan = idPlan,
                IdSede = idSede,
                FechaInicio = fechaInicio,
                FechaFin = fechaFin,
                Estado = estado,
                CreatedAt = ahora,
                UpdatedAt = ahora,
            }));
    }

    public static async Task ClearPermisosRolAsync(SecurityTestContext ctx)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM permisos_rol;"));
    }

    // ── biometrics.rs ────────────────────────────────────────────────────────

    public static Task<SecurityTestContext> NuevaDbAsync() => Task.FromResult(new SecurityTestContext());

    public static async Task<long> SedePrincipalAsync(SecurityTestContext ctx) =>
        (await ctx.Sedes.GetPrincipalAsync())!.IdSede;

    public static Task<long> InsertarPlanAsync(SecurityTestContext ctx) =>
        Fase4Helper.CrearPlanAsync(ctx, 30, 7, Precio);

    /// <summary>Socio de soporte (tabla de relación con id_sede_registro = sede).</summary>
    public static async Task InsertarSocioAsync(SecurityTestContext ctx, string idSocio, string nombre, long idSede)
    {
        var ahora = DateHelper.NowIsoUtc();
        await ctx.Socios.InsertAsync(new Socio
        {
            IdSocio = idSocio,
            Nombre = nombre,
            ApellidoPaterno = string.Empty,
            ApellidoMaterno = string.Empty,
            IdSedeRegistro = idSede,
            Estado = SocioEstados.Activo,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        });
    }

    /// <summary>INSERT directo de template biométrico activo (estado previo al enrollment).</summary>
    public static async Task InsertarTemplateAsync(SecurityTestContext ctx, string idSocio, string dedo, string path)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO socios_biometricos (id_registro, id_socio, dedo, archivo_template_path, " +
            "algoritmo_sdk, es_activa, created_at) " +
            "VALUES (@IdRegistro, @IdSocio, @Dedo, @Path, 'DPFP-10', 1, @CreatedAt);",
            new
            {
                IdRegistro = UuidHelper.NewV4(),
                IdSocio = idSocio,
                Dedo = dedo,
                Path = path,
                CreatedAt = DateHelper.NowIsoUtc(),
            }));
    }

    /// <summary>Registra una membresía con fecha_fin = hoy + 30d (escenario de sede/membresía).</summary>
    public static async Task InsertarMembresiaVigenteAsync(
        SecurityTestContext ctx,
        string idSocio,
        long idPlan,
        long idSede,
        string estado)
    {
        var ahora = DateHelper.NowIsoUtc();
        var fechaFin = DateHelper.ToIsoUtc(DateTime.UtcNow.AddDays(30));
        await InsertarMembresiaAsync(ctx, idSocio, idPlan, idSede, estado, ahora, fechaFin);
    }

    public static async Task<IReadOnlyList<string>> TemplatesPorSedeAsync(SecurityTestContext ctx, long idSede) =>
        await ctx.SociosBiometricos.GetTemplatePathsBySedeAsync(idSede);

    public static async Task<string?> TemplateActivoAsync(SecurityTestContext ctx, string idSocio)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT archivo_template_path FROM socios_biometricos " +
            "WHERE id_socio = @idSocio AND es_activa = 1 AND deleted_at IS NULL",
            new { idSocio }));
    }

    /// <summary>Conteo de templates activos (por socio o total si idSocio es null).</summary>
    public static async Task<int> CountActivasAsync(SecurityTestContext ctx, string? idSocio = null)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var sql = idSocio is null
            ? "SELECT COUNT(*) FROM socios_biometricos WHERE es_activa = 1 AND deleted_at IS NULL"
            : "SELECT COUNT(*) FROM socios_biometricos WHERE id_socio = @idSocio AND es_activa = 1 AND deleted_at IS NULL";
        return (int)await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, new { idSocio }));
    }
}