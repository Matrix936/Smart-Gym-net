using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase5;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Fase7;

/// <summary>Helpers compartidos por la cobranza portada (finance).</summary>
internal static class Fase7Helper
{
    public const long Precio = 10000;

    /// <summary>Superadmin + plan + socio + membresía con pago parcial (4000 → saldo 6000, parcial).</summary>
    public static async Task<(SecurityTestContext ctx, string token, long sedeId, string idSocio, string idCuenta)> CuentaConSaldoAsync()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var planId = await Fase4Helper.CrearPlanAsync(ctx, 30, 7, Precio);
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Ana"), sedeId);
        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);
        var membresia = await ctx.MembresiasService.VenderAsync(token, socio.IdSocio, planId, Fase4Helper.MetodoPago, 4000, sedeId);
        var cuenta = (await ctx.CuentasCobrar.GetByMembresiaAsync(membresia.IdMembresia))!;
        return (ctx, token, sedeId, socio.IdSocio, cuenta.IdCuenta);
    }

    public static async Task<CobroCuota?> CobroUnicoAsync(SecurityTestContext ctx, string idCuenta)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        return await conn.QuerySingleOrDefaultAsync<CobroCuota>(new CommandDefinition(
            "SELECT id_cobro, id_cuenta, monto_centavos, metodo_pago, fecha_cobro, " +
            "id_cobrador, resultado, updated_at, sincronizado, deleted_at " +
            "FROM cobros_cuotas WHERE id_cuenta = @idCuenta",
            new { idCuenta }));
    }

    public static async Task<int> CountRecordatoriosAsync(SecurityTestContext ctx, string idSocio)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        return (int)await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM cobros_recordatorios WHERE id_socio = @idSocio AND deleted_at IS NULL",
            new { idSocio }));
    }

    public static async Task<int> CountBitacoraAccionAsync(SecurityTestContext ctx, string accion, string idRegistroAfectado)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        return (int)await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM bitacora_auditoria " +
            "WHERE accion = @accion AND id_registro_afectado = @idRegistroAfectado",
            new { accion, idRegistroAfectado }));
    }
}