using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase5;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Accesos;

/// <summary>
/// Modalidad de registro de accesos (acceso.modo_registro): solo_entrada vs
/// entrada_y_salida (alternancia basada solo en concedidos) y ventana
/// anti-doble-toque de 60 segundos. La modalidad se lee en cada registro.
/// </summary>
public sealed class ModoRegistroAccesoTests
{
    private static async Task<(SecurityTestContext ctx, string token, long sedeId, string idSocio)> EscenarioAsync()
    {
        var (ctx, token, sedeId, idSocio, _, _) = await Fase5Helper.BaseAccessAsync();
        return (ctx, token, sedeId, idSocio);
    }

    /// <summary>Retrasa el timestamp de un registro para salir de la ventana anti-doble-toque.</summary>
    internal static async Task RetrocederTimestampAsync(SecurityTestContext ctx, string idAcceso, int minutos)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE accesos_bitacora SET timestamp = @ts WHERE id_acceso = @idAcceso",
            new { ts = DateHelper.ToIsoUtc(DateTime.UtcNow.AddMinutes(-minutos)), idAcceso }));
    }

    [Fact]
    public async Task solo_entrada_registra_todos_los_concedidos_como_entrada()
    {
        var (ctx, token, sedeId, idSocio) = await EscenarioAsync();
        ctx.Configuracion.SetAsync(AccesoModosRegistro.ClaveConfig, AccesoModosRegistro.SoloEntrada).GetAwaiter().GetResult();

        var r1 = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId);
        await RetrocederTimestampAsync(ctx, r1.IdAcceso, minutos: 5);
        var r2 = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId);

        var b1 = await ctx.Accesos.GetByIdAsync(r1.IdAcceso);
        var b2 = await ctx.Accesos.GetByIdAsync(r2.IdAcceso);
        Assert.Equal(AccesoTipos.Entrada, b1!.Tipo);
        Assert.Equal(AccesoTipos.Entrada, b2!.Tipo); // nunca alterna a salida
    }

    [Fact]
    public async Task entrada_y_salida_mantiene_alternancia_ignorando_denegados()
    {
        var (ctx, token, sedeId, idSocio) = await EscenarioAsync(); // default entrada_y_salida

        // 1er toque concedido → entrada.
        var r1 = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId);
        Assert.Equal(AccesoTipos.Entrada, (await ctx.Accesos.GetByIdAsync(r1.IdAcceso))!.Tipo);
        await RetrocederTimestampAsync(ctx, r1.IdAcceso, minutos: 5);

        // Toque intermedio DENEGADO (socio bloqueado): se registra pero NO participa en la alternancia.
        await ctx.SociosService.CambiarEstadoAsync(token, idSocio, SocioEstados.Bloqueado, "prueba");
        var rDenegado = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId);
        await RetrocederTimestampAsync(ctx, rDenegado.IdAcceso, minutos: 5);
        await ctx.SociosService.CambiarEstadoAsync(token, idSocio, SocioEstados.Activo, "prueba");

        // 3er toque concedido: el último CONCEDIDO fue entrada → toca salida
        // (con la lógica vieja, el denegado habría invertido el toggle).
        var r3 = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId);
        await RetrocederTimestampAsync(ctx, r3.IdAcceso, minutos: 5);
        var r4 = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId);

        Assert.Equal(AccesoTipos.Salida, (await ctx.Accesos.GetByIdAsync(r3.IdAcceso))!.Tipo);
        Assert.Equal(AccesoTipos.Entrada, (await ctx.Accesos.GetByIdAsync(r4.IdAcceso))!.Tipo);
    }

    [Fact]
    public async Task doble_toque_en_la_ventana_no_inserta_segundo_registro()
    {
        var (ctx, token, sedeId, idSocio) = await EscenarioAsync();

        var r1 = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId);
        var r2 = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId); // dentro de los 60 s

        // El resultado se recalcula igual (para refrescar el mensaje del Kiosco)…
        Assert.Equal(AccesoEstados.Concedido, r2.Estado);
        Assert.NotNull(r2.Socio);
        // …pero no hay segundo registro en bitácora.
        Assert.Null(await ctx.Accesos.GetByIdAsync(r2.IdAcceso));

        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var total = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM accesos_bitacora WHERE id_socio = @idSocio AND deleted_at IS NULL",
            new { idSocio }));
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task la_modalidad_se_lee_en_cada_registro_sin_cache()
    {
        var (ctx, token, sedeId, idSocio) = await EscenarioAsync(); // default entrada_y_salida

        // 1er registro bajo entrada_y_salida → entrada; sale de la ventana retrocediendo.
        var r1 = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId);
        await RetrocederTimestampAsync(ctx, r1.IdAcceso, minutos: 5);

        // Cambio de modalidad SIN volver a tocar nada más: debe aplicar al siguiente toque.
        ctx.Configuracion.SetAsync(AccesoModosRegistro.ClaveConfig, AccesoModosRegistro.SoloEntrada).GetAwaiter().GetResult();
        var r2 = await ctx.AccesoService.RegistrarAccesoKioskoAsync(idSocio, sedeId);

        // Con la lógica vieja habría alternado a salida; con solo_entrada es entrada.
        Assert.Equal(AccesoTipos.Entrada, (await ctx.Accesos.GetByIdAsync(r2.IdAcceso))!.Tipo);
    }
}
