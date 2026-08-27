using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Membresias;

/// <summary>
/// Venta de membresía suelta a crédito desde POS:
/// plazo custom, deuda vencida detectada, contado intacto, y
/// reparto proporcional en productos+membresía.
/// </summary>
public sealed class MembresiaCreditoTests
{
    [Fact]
    public async Task membresia_suelta_credito_con_plazo_custom()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Carlos"), sedeId);
        var planId = await Fase4Helper.CrearPlanAsync(ctx, 30, 7, 10000); // 30 días, $100.00

        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);

        // Vender membresía a crédito: paga $40.00 de $100.00, plazo 15 días
        var m = await ctx.MembresiasService.VenderAsync(
            token, socio.IdSocio, planId, "crédito", 4000, sedeId, plazoCreditoDias: 15);

        Assert.Equal(MembresiaEstados.Activa, m.Estado);

        // Verificar CuentaCobrar creada con saldo correcto
        var cuenta = await ctx.CuentasCobrar.GetByMembresiaAsync(m.IdMembresia);
        Assert.NotNull(cuenta);
        Assert.Equal(6000, cuenta!.SaldoPendienteCentavos); // 10000 - 4000
        Assert.Equal(CuentaCobrarEstados.Parcial, cuenta.Estado);

        // Verificar vencimiento = hoy + 15 días
        var vencimiento = DateHelper.ParseIsoUtc(cuenta.FechaVencimiento);
        var hoy = DateTime.UtcNow;
        var esperado = hoy.AddDays(15);
        Assert.True(Math.Abs((vencimiento - esperado).TotalDays) < 1,
            $"Vencimiento esperado ~{esperado:yyyy-MM-dd}, fue {vencimiento:yyyy-MM-dd}");
    }

    [Fact]
    public async Task deuda_vencida_detectada_para_venta_credito()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Deudor"), sedeId);
        var planId = await Fase4Helper.CrearPlanAsync(ctx, 30, 7, 10000);

        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);

        // 1ª venta: membresía a crédito con plazo corto (1 día)
        var m1 = await ctx.MembresiasService.VenderAsync(
            token, socio.IdSocio, planId, "crédito", 0, sedeId, plazoCreditoDias: 1);
        Assert.Equal(MembresiaEstados.Activa, m1.Estado);

        // Forzar vencimiento: actualizar fecha_vencimiento a ayer
        var cuenta = (await ctx.CuentasCobrar.GetByMembresiaAsync(m1.IdMembresia))!;
        var ayer = DateHelper.ToIsoUtc(DateTime.UtcNow.AddDays(-1));
        await ctx.CuentasCobrar.ActualizarFechaVencimientoAsync(cuenta.IdCuenta, ayer);

        // Verificar que la deuda vencida se detecta correctamente
        var hoy = DateHelper.NowIsoUtc();
        var tieneDeudaVencida = await ctx.CuentasCobrar.SocioTieneDeudaVencidaAsync(socio.IdSocio, hoy);
        Assert.True(tieneDeudaVencida, "El socio debería tener deuda vencida detectada");

        // Verificar que socio sin deuda vencida NO es bloqueado
        var socio2 = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Limpio"), sedeId);
        var sinDeuda = await ctx.CuentasCobrar.SocioTieneDeudaVencidaAsync(socio2.IdSocio, hoy);
        Assert.False(sinDeuda, "Un socio sin deudas no debería ser bloqueado");

        // Verificar que al registrar un abono que cubre la deuda, deja de detectarse
        // (CuentaCobrar actualizada a "pagada")
        await ctx.CuentasCobrar.CambiarEstadoAsync(cuenta.IdCuenta, CuentaCobrarEstados.Cobrada, DateHelper.NowIsoUtc());
        var despuesAbono = await ctx.CuentasCobrar.SocioTieneDeudaVencidaAsync(socio.IdSocio, DateHelper.NowIsoUtc());
        Assert.False(despuesAbono, "Después de pagar la deuda, no debería detectarse como vencida");
    }

    [Fact]
    public async Task membresia_suelta_contado_comportamiento_intacto()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Efectivo"), sedeId);
        var planId = await Fase4Helper.CrearPlanAsync(ctx, 30, 7, 10000);

        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);

        // Vender membresía de contado: precio completo
        var m = await ctx.MembresiasService.VenderAsync(
            token, socio.IdSocio, planId, "efectivo", 10000, sedeId);

        Assert.Equal(MembresiaEstados.Activa, m.Estado);

        // NO debe crear CuentaCobrar (pago completo)
        var cuenta = await ctx.CuentasCobrar.GetByMembresiaAsync(m.IdMembresia);
        Assert.Null(cuenta);

        // Verificar movimiento de caja por el monto completo
        var pagos = await ctx.Pagos.GetByMembresiaAsync(m.IdMembresia);
        Assert.Single(pagos);
        Assert.Equal(10000, pagos[0].MontoCentavos);
    }

    [Fact]
    public async Task productos_y_membresia_ambos_credito_saldo_correcto()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var socio = await ctx.SociosService.CrearSocioAsync(token, Fase4Helper.DatosSocio("Mixto"), sedeId);
        var planId = await Fase4Helper.CrearPlanAsync(ctx, 30, 7, 8000); // $80.00

        // Crear producto directamente en DB
        var productoId = await ctx.Productos.InsertAsync(new Producto
        {
            Descripcion = "Protein",
            PrecioVentaCentavos = 5000, // $50.00
            RequiereInventario = true,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });

        await ctx.CajaService.AbrirCajaAsync(token, 0, sedeId);

        // Agregar stock al producto
        await ctx.Inventario.InsertAsync(new InventarioSucursal
        {
            IdProducto = productoId,
            IdSede = sedeId,
            Stock = 100,
            StockMinimo = 1,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });

        // Habilitar crédito
        await ctx.EmpresaConfigService.ActualizarPosPermiteCreditoAsync(token, true);

        // Vender producto a crédito: paga $30.00 de $50.00
        var ventaInput = new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = productoId, Cantidad = 1 }],
            IdSocio = socio.IdSocio,
            MetodoPago = "crédito",
            MontoPagadoCentavos = 3000,
            PlazoCreditoDias = 20,
        };
        var venta = await ctx.PosService.RegistrarVentaAsync(token, ventaInput, sedeId);

        // Verificar venta de productos
        Assert.Equal(5000, venta.TotalCentavos);
        Assert.Equal(2000, venta.SaldoPendienteCentavos); // 5000 - 3000
        Assert.NotNull(venta.SaldoVenceIsoUtc);

        // Vender membresía a crédito: paga $20.00 de $80.00
        var membresia = await ctx.MembresiasService.VenderAsync(
            token, socio.IdSocio, planId, "crédito", 2000, sedeId, plazoCreditoDias: 15);

        Assert.Equal(MembresiaEstados.Activa, membresia.Estado);

        // Verificar CuentaCobrar de membresía
        var cuentaMembresia = await ctx.CuentasCobrar.GetByMembresiaAsync(membresia.IdMembresia);
        Assert.NotNull(cuentaMembresia);
        Assert.Equal(6000, cuentaMembresia!.SaldoPendienteCentavos); // 8000 - 2000

        // Verificar CuentaCobrar de productos
        var cuentaPos = await ctx.CuentasCobrar.GetPorVentaAsync(venta.IdVenta);
        Assert.NotNull(cuentaPos);
        Assert.Equal(2000, cuentaPos!.SaldoPendienteCentavos); // 5000 - 3000

        // Saldo total pendiente: 2000 (productos) + 6000 (membresía) = 8000
        Assert.Equal(8000, cuentaPos.SaldoPendienteCentavos + cuentaMembresia.SaldoPendienteCentavos);
    }
}
