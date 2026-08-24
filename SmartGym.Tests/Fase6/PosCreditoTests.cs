using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Fase6;

/// <summary>
/// Venta a crédito en POS: interruptor global pos.permite_credito, gate de
/// deuda vencida del socio y creación de CuentaCobrar (mismo patrón que
/// MembresiasService.VenderAsync). El pago completo no se ve afectado.
/// </summary>
public sealed class PosCreditoTests
{
    [Fact]
    public async Task credito_apagado_pago_incompleto_es_rechazado()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
            {
                Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
                MetodoPago = "efectivo",
                MontoPagadoCentavos = Fase6Helper.PrecioProteina - 20000,
            }, sedeId));

        Assert.Equal(BusinessError.Conflict, ex.Error);
        Assert.Equal("pago_incompleto_no_permitido", ex.Code);
        Assert.Equal(0, await ContarVentasAsync(ctx));
    }

    [Fact]
    public async Task credito_encendido_socio_sin_deuda_crea_cuenta_cobrar()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        await ctx.Configuracion.SetAsync("pos.permite_credito", "true");
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var pagado = Fase6Helper.PrecioProteina - 30000;
        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            IdSocio = idSocio,
            MetodoPago = "efectivo",
            MontoPagadoCentavos = pagado,
        }, sedeId);

        Assert.Equal(Fase6Helper.PrecioProteina, venta.TotalCentavos);
        Assert.Equal(pagado, venta.MontoPagadoCentavos);
        Assert.Equal(30000, venta.SaldoPendienteCentavos);

        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        var cuenta = await conn.QuerySingleOrDefaultAsync<CuentaCobrar>(
            new CommandDefinition(
                "SELECT * FROM cuentas_cobrar WHERE id_socio = @idSocio AND deleted_at IS NULL",
                new { idSocio }));
        Assert.NotNull(cuenta);
        Assert.Null(cuenta!.IdMembresia);
        Assert.Equal(CuentaCobrarOrigenes.Pos, cuenta.Origen);
        Assert.Equal(30000, cuenta.SaldoPendienteCentavos);
        Assert.Equal(CuentaCobrarEstados.Parcial, cuenta.Estado);

        // Vence a futuro (~15 días) y a caja solo entró lo pagado.
        Assert.True(DateHelper.ParseIsoUtc(cuenta.FechaVencimiento) > DateTime.UtcNow);
        var ingresoCaja = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT monto_centavos FROM caja_movimientos WHERE referencia_id = @idVenta",
            new { idVenta = venta.IdVenta }));
        Assert.Equal(pagado, ingresoCaja);
    }

    [Fact]
    public async Task credito_encendido_socio_con_deuda_vencida_es_rechazado()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        await InsertarCuentaAsync(ctx, idSocio, vencimiento: DateHelper.ToIsoUtc(DateTime.UtcNow.AddDays(-1)));
        await ctx.Configuracion.SetAsync("pos.permite_credito", "true");
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
            {
                Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
                IdSocio = idSocio,
                MetodoPago = "efectivo",
                MontoPagadoCentavos = 1,
            }, sedeId));

        Assert.Equal(BusinessError.Conflict, ex.Error);
        Assert.Equal("socio_tiene_deuda_vencida", ex.Code);
        Assert.Equal(0, await ContarVentasAsync(ctx));
    }

    [Fact]
    public async Task credito_encendido_deuda_no_vencida_no_bloquea()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        await InsertarCuentaAsync(ctx, idSocio, vencimiento: DateHelper.ToIsoUtc(DateTime.UtcNow.AddDays(30)));
        await ctx.Configuracion.SetAsync("pos.permite_credito", "true");
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            IdSocio = idSocio,
            MetodoPago = "efectivo",
            MontoPagadoCentavos = 1,
        }, sedeId);

        Assert.Equal(VentaEstados.Completada, venta.Estado);
        Assert.Equal(2, await ContarCuentasAsync(ctx, idSocio));
    }

    [Fact]
    public async Task credito_requiere_socio_asociado()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.Configuracion.SetAsync("pos.permite_credito", "true");
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
            {
                Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
                MetodoPago = "efectivo",
                MontoPagadoCentavos = 1,
            }, sedeId));

        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("socio_requerido_credito", ex.Code);
    }

    /// <summary>Aislamiento: pago completo ni siquiera consulta el interruptor ni las deudas.</summary>
    [Fact]
    public async Task pago_completo_procede_con_credito_apagado_y_deuda_vencida()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        await InsertarCuentaAsync(ctx, idSocio, vencimiento: DateHelper.ToIsoUtc(DateTime.UtcNow.AddDays(-5)));
        // pos.permite_credito NO existe (default apagado).
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            IdSocio = idSocio,
            MetodoPago = "efectivo",
            MontoPagadoCentavos = Fase6Helper.PrecioProteina,
        }, sedeId);

        Assert.Equal(Fase6Helper.PrecioProteina, venta.TotalCentavos);
        Assert.Equal(0, venta.SaldoPendienteCentavos);
        Assert.Equal(1, await ContarCuentasAsync(ctx, idSocio));
    }

    [Fact]
    public async Task monto_pagado_invalido_da_validation()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.Configuracion.SetAsync("pos.permite_credito", "true");
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var exExcesivo = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
            {
                Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
                MetodoPago = "efectivo",
                MontoPagadoCentavos = Fase6Helper.PrecioProteina + 1,
            }, sedeId));
        Assert.Equal("monto_excesivo", exExcesivo.Code);

        var exNegativo = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
            {
                Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
                MetodoPago = "efectivo",
                MontoPagadoCentavos = -1,
            }, sedeId));
        Assert.Equal("monto_invalido", exNegativo.Code);
    }

    [Fact]
    public async Task interruptor_pos_credito_se_persiste_y_se_lee()
    {
        var (ctx, token, _) = await Fase4Helper.SuperadminAsync();

        Assert.False(await ctx.EmpresaConfigService.ObtenerPosPermiteCreditoAsync(token));

        await ctx.EmpresaConfigService.ActualizarPosPermiteCreditoAsync(token, permite: true);
        Assert.True(await ctx.EmpresaConfigService.ObtenerPosPermiteCreditoAsync(token));
        Assert.True(await ctx.PosService.ObtenerPermiteCreditoAsync(token));

        await ctx.EmpresaConfigService.ActualizarPosPermiteCreditoAsync(token, permite: false);
        Assert.False(await ctx.PosService.ObtenerPermiteCreditoAsync(token));
    }

    // ---------------------------------------------------------------- helpers

    private static Task InsertarCuentaAsync(SecurityTestContext ctx, string idSocio, string vencimiento)
    {
        return EjecutarAsync(ctx,
            "INSERT INTO cuentas_cobrar (id_cuenta, id_membresia, origen, id_socio, saldo_pendiente_centavos, " +
            "fecha_vencimiento, estado, updated_at, sincronizado) " +
            "VALUES (@id, NULL, 'membresia', @idSocio, 50000, @vencimiento, 'pendiente', @ahora, 0)",
            new { id = UuidHelper.NewV4(), idSocio, vencimiento, ahora = DateHelper.NowIsoUtc() });
    }

    private static async Task<int> ContarVentasAsync(SecurityTestContext ctx)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(1) FROM ventas"));
    }

    private static async Task<int> ContarCuentasAsync(SecurityTestContext ctx, string idSocio)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(1) FROM cuentas_cobrar WHERE id_socio = @idSocio AND deleted_at IS NULL",
            new { idSocio }));
    }

    private static async Task EjecutarAsync(SecurityTestContext ctx, string sql, object param)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        await conn.ExecuteAsync(new CommandDefinition(sql, param));
    }
}
