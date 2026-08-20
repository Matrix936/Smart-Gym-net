using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase5;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Fase7;

/// <summary>Flujo completo de cobranza (finance): abonos + recordatorios.</summary>
public sealed class CobranzaTests
{
    [Fact]
    public async Task registrar_abono_exitoso_resta_saldo_y_marca_cobrada()
    {
        var (ctx, token, sedeId, _, idCuenta) = await Fase7Helper.CuentaConSaldoAsync();

        var cuenta = await ctx.CobranzaService.RegistrarAbonoAsync(token, idCuenta, 6000, "efectivo", sedeId);

        Assert.Equal(0, cuenta.SaldoPendienteCentavos);
        Assert.Equal(CuentaCobrarEstados.Cobrada, cuenta.Estado);

        var cobro = await Fase7Helper.CobroUnicoAsync(ctx, idCuenta);
        Assert.NotNull(cobro);
        Assert.Equal(6000, cobro!.MontoCentavos);
        Assert.Equal(CobroCuotaResultados.Exitoso, cobro.Resultado);

        var caja = (await ctx.CajaService.ObtenerCajaAbiertaAsync(token, sedeId))!;
        var movimientos = await ctx.Movimientos.GetBySesionAsync(caja.IdSesion);
        var abono = Assert.Single(movimientos, m => m.ReferenciaTipo == CajaReferenciaTipos.Abono);
        Assert.Equal(MovimientoTipos.Ingreso, abono.Tipo);
        Assert.Equal(cobro.IdCobro, abono.ReferenciaId);
        Assert.Equal(6000, abono.MontoCentavos);
        Assert.True(abono.AfectaEfectivo);
    }

    [Fact]
    public async Task registrar_abono_parcial_deja_cuenta_parcial_y_movimiento_no_afecta_efectivo()
    {
        var (ctx, token, sedeId, _, idCuenta) = await Fase7Helper.CuentaConSaldoAsync();

        var cuenta = await ctx.CobranzaService.RegistrarAbonoAsync(token, idCuenta, 2000, "tarjeta", sedeId);

        Assert.Equal(4000, cuenta.SaldoPendienteCentavos);
        Assert.Equal(CuentaCobrarEstados.Parcial, cuenta.Estado);

        var caja = (await ctx.CajaService.ObtenerCajaAbiertaAsync(token, sedeId))!;
        var movimientos = await ctx.Movimientos.GetBySesionAsync(caja.IdSesion);
        var abono = Assert.Single(movimientos, m => m.ReferenciaTipo == CajaReferenciaTipos.Abono);
        Assert.Equal(2000, abono.MontoCentavos);
        Assert.Equal("tarjeta", abono.MetodoPago);
        Assert.False(abono.AfectaEfectivo);
    }

    [Fact]
    public async Task registrar_abono_sin_caja_abierta_da_conflict()
    {
        var (ctx, token, sedeId, _, idCuenta) = await Fase7Helper.CuentaConSaldoAsync();
        var caja = (await ctx.CajaService.ObtenerCajaAbiertaAsync(token, sedeId))!;
        await ctx.CajaService.CerrarCajaAsync(token, caja.IdSesion, 4000);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CobranzaService.RegistrarAbonoAsync(token, idCuenta, 1000, "efectivo", sedeId));
        Assert.Equal(BusinessError.Conflict, ex.Error);
        Assert.Equal("caja_no_abierta", ex.Code);
    }

    [Fact]
    public async Task registrar_abono_monto_excede_saldo_da_validation()
    {
        var (ctx, token, sedeId, _, idCuenta) = await Fase7Helper.CuentaConSaldoAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CobranzaService.RegistrarAbonoAsync(token, idCuenta, 7000, "efectivo", sedeId));
        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("monto_excesivo", ex.Code);
    }

    [Fact]
    public async Task registrar_abono_cuenta_inexistente_da_not_found()
    {
        var (ctx, token, sedeId, _, _) = await Fase7Helper.CuentaConSaldoAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CobranzaService.RegistrarAbonoAsync(token, UuidHelper.NewV4(), 1000, "efectivo", sedeId));
        Assert.Equal(BusinessError.NotFound, ex.Error);
        Assert.Equal("cuenta_no_encontrada", ex.Code);
    }

    [Fact]
    public async Task registrar_abono_cuenta_ya_cobrada_da_conflict()
    {
        var (ctx, token, sedeId, _, idCuenta) = await Fase7Helper.CuentaConSaldoAsync();
        await ctx.CobranzaService.RegistrarAbonoAsync(token, idCuenta, 6000, "efectivo", sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CobranzaService.RegistrarAbonoAsync(token, idCuenta, 1000, "efectivo", sedeId));
        Assert.Equal(BusinessError.Conflict, ex.Error);
        Assert.Equal("cuenta_no_activa", ex.Code);
    }

    [Fact]
    public async Task registrar_abono_monto_negativo_o_cero_da_validation()
    {
        var (ctx, token, sedeId, _, idCuenta) = await Fase7Helper.CuentaConSaldoAsync();

        var cero = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CobranzaService.RegistrarAbonoAsync(token, idCuenta, 0, "efectivo", sedeId));
        Assert.Equal(BusinessError.Validation, cero.Error);
        Assert.Equal("monto_invalido", cero.Code);

        var negativo = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CobranzaService.RegistrarAbonoAsync(token, idCuenta, -1, "efectivo", sedeId));
        Assert.Equal(BusinessError.Validation, negativo.Error);
        Assert.Equal("monto_invalido", negativo.Code);
    }

    [Fact]
    public async Task registrar_abono_registra_bitacora_auditoria()
    {
        var (ctx, token, sedeId, _, idCuenta) = await Fase7Helper.CuentaConSaldoAsync();

        await ctx.CobranzaService.RegistrarAbonoAsync(token, idCuenta, 2000, "efectivo", sedeId);

        Assert.Equal(1, await Fase7Helper.CountBitacoraAccionAsync(ctx, "cobranza.abono", idCuenta));
    }

    [Fact]
    public async Task registrar_recordatorio_envio_guardado()
    {
        var (ctx, token, sedeId, _, _) = await Fase7Helper.CuentaConSaldoAsync();
        var idSocio = UuidHelper.NewV4();
        await Fase5Helper.InsertarSocioAsync(ctx, idSocio, "Bruno", sedeId);

        var recordatorio = await ctx.CobranzaService.RegistrarRecordatorioAsync(token, idSocio, "whatsapp");

        Assert.Equal(idSocio, recordatorio.IdSocio);
        Assert.Equal("whatsapp", recordatorio.Tipo);
        Assert.Equal(CobroRecordatorioResultados.Enviado, recordatorio.Resultado);
        Assert.Equal(1, await Fase7Helper.CountRecordatoriosAsync(ctx, idSocio));
        Assert.Equal(1, await Fase7Helper.CountBitacoraAccionAsync(ctx, "cobranza.recordatorio", recordatorio.IdRecordatorio));
    }

    [Fact]
    public async Task registrar_recordatorio_tipo_invalido_da_validation()
    {
        var (ctx, token, sedeId, _, _) = await Fase7Helper.CuentaConSaldoAsync();
        var idSocio = UuidHelper.NewV4();
        await Fase5Helper.InsertarSocioAsync(ctx, idSocio, "Carla", sedeId);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CobranzaService.RegistrarRecordatorioAsync(token, idSocio, "correo"));
        Assert.Equal(BusinessError.Validation, ex.Error);
        Assert.Equal("tipo_invalido", ex.Code);
    }

    [Fact]
    public async Task registrar_recordatorio_socio_inexistente_da_not_found()
    {
        var (ctx, token, _, _, _) = await Fase7Helper.CuentaConSaldoAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.CobranzaService.RegistrarRecordatorioAsync(token, UuidHelper.NewV4(), "email"));
        Assert.Equal(BusinessError.NotFound, ex.Error);
        Assert.Equal("socio_no_encontrado", ex.Code);
    }
}