using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase6;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Fase6;

/// <summary>
/// Cancelación de ventas a crédito: la cuenta por cobrar asociada se ANULA si
/// no tiene abonos; con abonos la cancelación se rechaza (dinero ya cobrado
/// que requeriría reversión manual). El pago completo no se ve afectado.
/// </summary>
public sealed class CancelacionCreditoTests
{
    /// <summary>Vende a crédito (pago parcial) con crédito habilitado y caja abierta.</summary>
    private static async Task<(SecurityTestContext ctx, string token, long sedeId, string idSocio, VentaInfo venta)>
        VenderACreditoAsync()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        await ctx.Configuracion.SetAsync("pos.permite_credito", "true");
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            IdSocio = idSocio,
            MetodoPago = MetodosPago.Efectivo,
            MontoPagadoCentavos = Fase6Helper.PrecioProteina - 20000,
        }, sedeId);
        return (ctx, token, sedeId, idSocio, venta);
    }

    private static async Task<CuentaCobrar> CuentaDeAsync(SecurityTestContext ctx, string idVenta)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        return await conn.QuerySingleAsync<CuentaCobrar>(new CommandDefinition(
            "SELECT * FROM cuentas_cobrar WHERE id_venta = @idVenta AND deleted_at IS NULL",
            new { idVenta }));
    }

    [Fact]
    public async Task cancelar_credito_sin_abonos_anula_la_cuenta()
    {
        var (ctx, token, sedeId, idSocio, venta) = await VenderACreditoAsync();

        await ctx.PosService.CancelarVentaAsync(token, new CancelarVentaInput
        {
            IdVenta = venta.IdVenta,
            PasswordConfirmacion = Fase4Helper.Password,
        }, sedeId);

        var cuenta = await CuentaDeAsync(ctx, venta.IdVenta);
        Assert.Equal(CuentaCobrarEstados.Anulada, cuenta.Estado);

        // Ya no es deuda vencida para nuevas ventas a crédito del mismo socio.
        Assert.False(await ctx.CuentasCobrar.SocioTieneDeudaVencidaAsync(
            idSocio, DateHelper.NowIsoUtc()));

        // Y no aparece en el listado de /cobranza.
        var listado = await ctx.CuentasCobrar.BuscarAsync(sedeId, null, null, 1, 10);
        Assert.DoesNotContain(listado.Items, c => c.IdCuenta == cuenta.IdCuenta);

        // Bitácora de la anulación.
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var accion = await conn.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT accion FROM bitacora_auditoria WHERE accion = 'cobranza.cuenta_anulada' " +
            "AND id_registro_afectado = @idCuenta",
            new { idCuenta = cuenta.IdCuenta }));
        Assert.Equal("cobranza.cuenta_anulada", accion);
    }

    [Fact]
    public async Task cancelar_credito_con_abono_es_rechazado_y_no_revierte_nada()
    {
        var (ctx, token, sedeId, idSocio, venta) = await VenderACreditoAsync();
        var cuenta = await CuentaDeAsync(ctx, venta.IdVenta);

        // Abono parcial sobre la cuenta.
        await ctx.CobranzaService.RegistrarAbonoAsync(
            token, cuenta.IdCuenta, 10000, "efectivo", sedeId);

        var movimientosAntes = await ContarMovimientosAsync(ctx);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.CancelarVentaAsync(token, new CancelarVentaInput
            {
                IdVenta = venta.IdVenta,
                PasswordConfirmacion = Fase4Helper.Password,
            }, sedeId));

        Assert.Equal(BusinessError.Conflict, ex.Error);
        Assert.Equal("venta_con_abonos_no_cancelable", ex.Code);

        // Nada se revirtió: venta sigue completada, cuenta sigue parcial,
        // stock intacto y ningún movimiento de egreso nuevo.
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var estadoVenta = await Fase6Helper.EstadoVentaAsync(ctx, venta.IdVenta);
        Assert.Equal(VentaEstados.Completada, estadoVenta);

        var estadoCuenta = await conn.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT estado FROM cuentas_cobrar WHERE id_cuenta = @idCuenta",
            new { idCuenta = cuenta.IdCuenta }));
        Assert.Equal(CuentaCobrarEstados.Parcial, estadoCuenta);

        Assert.Equal(movimientosAntes, await ContarMovimientosAsync(ctx));
    }

    [Fact]
    public async Task cancelar_pago_completo_sin_cuenta_sigue_funcionando_normal()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        await ctx.Configuracion.SetAsync("pos.permite_credito", "true");
        await ctx.CajaService.AbrirCajaAsync(token, 1000000, sedeId);

        // Pago COMPLETO: no genera cuenta aunque el socio exista.
        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            IdSocio = idSocio,
            MetodoPago = MetodosPago.Efectivo,
        }, sedeId);

        Assert.Empty(await CuentasDeVentaAsync(ctx, venta.IdVenta));

        await ctx.PosService.CancelarVentaAsync(token, new CancelarVentaInput
        {
            IdVenta = venta.IdVenta,
            PasswordConfirmacion = Fase4Helper.Password,
        }, sedeId);

        Assert.Equal(VentaEstados.Cancelada, await Fase6Helper.EstadoVentaAsync(ctx, venta.IdVenta));
        Assert.Equal(10, await Fase6Helper.StockAsync(ctx, idProducto, sedeId));
        Assert.Empty(await CuentasDeVentaAsync(ctx, venta.IdVenta));
    }

    private static async Task<IEnumerable<CuentaCobrar>> CuentasDeVentaAsync(SecurityTestContext ctx, string idVenta)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var rows = await conn.QueryAsync<CuentaCobrar>(new CommandDefinition(
            "SELECT * FROM cuentas_cobrar WHERE id_venta = @idVenta AND deleted_at IS NULL",
            new { idVenta }));
        return rows;
    }

    private static async Task<int> ContarMovimientosAsync(SecurityTestContext ctx)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM caja_movimientos"));
    }
}
