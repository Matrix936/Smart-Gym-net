using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Fase5;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Fase6;

/// <summary>
/// Historial unificado de ventas: caja_movimientos con joins polimórficos
/// (venta POS, cancelación, pago de membresía, abono).
/// </summary>
public sealed class HistorialVentasTests
{
    [Fact]
    public async Task historial_lista_venta_con_datos_resueltos()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            IdSocio = idSocio,
            MetodoPago = "efectivo",
        }, sedeId);

        var pagina = await ctx.VentasService.BuscarHistorialAsync(token, idSedeFrontend: sedeId);

        Assert.Equal(1, pagina.TotalRegistros);
        var fila = Assert.Single(pagina.Items);
        Assert.Equal(CajaReferenciaTipos.Venta, fila.ReferenciaTipo);
        Assert.Equal("ingreso", fila.TipoMovimiento);
        Assert.Equal(Fase6Helper.PrecioProteina, fila.MontoCentavos);
        Assert.Equal("efectivo", fila.MetodoPago);
        Assert.Equal(idSocio, fila.IdSocio);
        Assert.Equal("Juan", fila.NombreSocio);
        Assert.Equal(VentaEstados.Completada, fila.EstadoVenta);
        Assert.True(fila.EsVentaCancelable);
    }

    [Fact]
    public async Task cancelar_venta_deja_una_sola_fila_con_estado_cancelada()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "tarjeta",
        }, sedeId);

        await ctx.PosService.CancelarVentaAsync(token, new CancelarVentaInput
        {
            IdVenta = venta.IdVenta,
            PasswordConfirmacion = Fase4Helper.Password,
        }, sedeId);

        var pagina = await ctx.VentasService.BuscarHistorialAsync(token, idSedeFrontend: sedeId);

        // Una sola fila por venta: la cancelación ya no es fila propia —
        // el egreso del reembolso es dato de Caja/Finanzas.
        Assert.Equal(1, pagina.TotalRegistros);
        Assert.All(pagina.Items, f => Assert.Equal("tarjeta", f.MetodoPago));

        var fila = pagina.Items.Single(f => f.ReferenciaTipo == CajaReferenciaTipos.Venta);
        Assert.Equal("ingreso", fila.TipoMovimiento);
        Assert.Equal(Fase6Helper.PrecioProteina, fila.MontoCentavos);
        Assert.Equal(VentaEstados.Cancelada, fila.EstadoVenta);
        Assert.False(fila.EsVentaCancelable);

        // El detalle conserva quién y cuándo canceló (dato que antes vivía en
        // la fila de la cancelación).
        var detalle = await ctx.VentasService.ObtenerDetalleVentaAsync(token, venta.IdVenta, sedeId);
        Assert.Equal(VentaEstados.Cancelada, detalle.Estado);
        Assert.False(string.IsNullOrWhiteSpace(detalle.CanceladaPor));
        Assert.False(string.IsNullOrWhiteSpace(detalle.CanceladaElIsoUtc));
    }

    [Fact]
    public async Task filtro_por_tipo_referencia()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);
        await VenderMembresiaAsync(ctx, token, sedeId);
        await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sedeId);

        var soloVentas = await ctx.VentasService.BuscarHistorialAsync(
            token, new HistorialFiltros { TipoReferencia = CajaReferenciaTipos.Venta }, idSedeFrontend: sedeId);

        Assert.True(soloVentas.TotalRegistros >= 1);
        Assert.All(soloVentas.Items, f => Assert.Equal(CajaReferenciaTipos.Venta, f.ReferenciaTipo));

        var soloPagos = await ctx.VentasService.BuscarHistorialAsync(
            token, new HistorialFiltros { TipoReferencia = CajaReferenciaTipos.PagoMembresia }, idSedeFrontend: sedeId);

        Assert.Equal(1, soloPagos.TotalRegistros);
        Assert.False(soloPagos.Items[0].EsVentaCancelable);
    }

    [Fact]
    public async Task filtro_por_metodo_pago()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);
        await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sedeId);
        await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "tarjeta",
        }, sedeId);

        var tarjeta = await ctx.VentasService.BuscarHistorialAsync(
            token, new HistorialFiltros { MetodoPago = "tarjeta" }, idSedeFrontend: sedeId);

        Assert.Equal(1, tarjeta.TotalRegistros);
        Assert.Equal("tarjeta", tarjeta.Items[0].MetodoPago);
    }

    [Fact]
    public async Task filtro_por_estado_venta_excluye_otros_tipos()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sedeId);
        await ctx.PosService.CancelarVentaAsync(token, new CancelarVentaInput
        {
            IdVenta = venta.IdVenta,
            PasswordConfirmacion = Fase4Helper.Password,
        }, sedeId);

        var completadas = await ctx.VentasService.BuscarHistorialAsync(
            token, new HistorialFiltros { EstadoVenta = VentaEstados.Completada }, idSedeFrontend: sedeId);
        Assert.Equal(0, completadas.TotalRegistros);

        var canceladas = await ctx.VentasService.BuscarHistorialAsync(
            token, new HistorialFiltros { EstadoVenta = VentaEstados.Cancelada }, idSedeFrontend: sedeId);
        // Una sola fila por venta cancelada (el egreso ya no es fila propia).
        Assert.Equal(1, canceladas.TotalRegistros);
        Assert.All(canceladas.Items, f => Assert.Equal(VentaEstados.Cancelada, f.EstadoVenta));
    }

    [Fact]
    public async Task filtro_por_socio()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            IdSocio = idSocio,
            MetodoPago = "efectivo",
        }, sedeId);
        await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sedeId);

        var delSocio = await ctx.VentasService.BuscarHistorialAsync(
            token, new HistorialFiltros { IdSocio = idSocio }, idSedeFrontend: sedeId);

        Assert.Equal(1, delSocio.TotalRegistros);
        Assert.Equal(idSocio, delSocio.Items[0].IdSocio);
    }

    [Fact]
    public async Task paginacion_y_tamano_invalido()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);
        var sesion = (await ctx.Cajas.GetAbiertaPorSedeAsync(sedeId))!;

        // 12 movimientos directos (rápidos) para probar dos páginas de 10.
        for (var i = 0; i < 12; i++)
        {
            await ctx.Movimientos.InsertAsync(new CajaMovimiento
            {
                IdMovimiento = $"mov_{i}_{UuidHelper.NewV4()}",
                IdSesion = sesion.IdSesion,
                Tipo = MovimientoTipos.Ingreso,
                Concepto = "venta",
                MontoCentavos = 1000 + i,
                MetodoPago = "efectivo",
                AfectaEfectivo = true,
                ReferenciaTipo = CajaReferenciaTipos.Venta,
                ReferenciaId = $"ven_{i}",
                CreatedAt = DateHelper.NowIsoUtc(),
                UpdatedAt = DateHelper.NowIsoUtc(),
            });
        }

        var pagina1 = await ctx.VentasService.BuscarHistorialAsync(token, pagina: 1, tamanoPagina: 10, idSedeFrontend: sedeId);
        Assert.Equal(12, pagina1.TotalRegistros);
        Assert.Equal(10, pagina1.Items.Count);
        Assert.Equal(2, pagina1.TotalPaginas);

        var pagina2 = await ctx.VentasService.BuscarHistorialAsync(token, pagina: 2, tamanoPagina: 10, idSedeFrontend: sedeId);
        Assert.Equal(2, pagina2.Items.Count);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            ctx.VentasService.BuscarHistorialAsync(token, tamanoPagina: 13, idSedeFrontend: sedeId));
    }

    [Fact]
    public async Task abono_aparece_con_socio_resuelto()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        var idMembresia = await VenderMembresiaAsync(ctx, token, sedeId);

        var idCuenta = $"cta_{UuidHelper.NewV4()}";
        await using (var conn = ConnectionFactory.Open(ctx.DbPath))
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO cuentas_cobrar (id_cuenta, id_membresia, id_socio, saldo_pendiente_centavos, " +
                "fecha_vencimiento, estado, updated_at, sincronizado) " +
                "VALUES (@id, @idMembresia, @idSocio, 5000, '2099-01-01T00:00:00Z', 'pendiente', " +
                "@ahora, 0)",
                new { id = idCuenta, idMembresia, idSocio, ahora = DateHelper.NowIsoUtc() }));
        }

        await ctx.CobranzaService.RegistrarAbonoAsync(token, idCuenta, 5000, "efectivo", sedeId);

        var abonos = await ctx.VentasService.BuscarHistorialAsync(
            token, new HistorialFiltros { TipoReferencia = CajaReferenciaTipos.Abono }, idSedeFrontend: sedeId);

        var abono = Assert.Single(abonos.Items);
        Assert.Equal(5000, abono.MontoCentavos);
        Assert.Equal("efectivo", abono.MetodoPago);
        Assert.Equal(idSocio, abono.IdSocio);
        Assert.Equal("Juan", abono.NombreSocio);
        Assert.Null(abono.EstadoVenta);
        Assert.False(abono.EsVentaCancelable);
    }

    [Fact]
    public async Task sin_permiso_ver_historial_falla()
    {
        var (ctx, token, sedeId, _) = await Fase6Helper.BaseAsync();
        await Fase5Helper.ClearPermisosRolAsync(ctx);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.VentasService.BuscarHistorialAsync(token, idSedeFrontend: sedeId));

        Assert.Equal(BusinessError.Unauthorized, ex.Error);
        Assert.Equal("sin_permiso", ex.Code);
    }

    [Fact]
    public async Task detalle_de_otra_sede_da_no_encontrada()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        var otraSede = await ctx.Sedes.InsertAsync(new Sede
        {
            Nombre = "Otra Sede",
            EsActiva = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 1 }],
            MetodoPago = "efectivo",
        }, sedeId);

        // Consulta desde la otra sede: la venta no debe ser visible fuera
        // de su sede (mismo código que inexistente, para no filtrar datos).
        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => ctx.VentasService.ObtenerDetalleVentaAsync(token, venta.IdVenta, otraSede));
        Assert.Equal("venta_no_encontrada", ex.Code);
    }

    [Fact]
    public async Task detalle_venta_incluye_descripcion_producto()
    {
        var (ctx, token, sedeId, idProducto) = await Fase6Helper.BaseAsync();
        await ctx.CajaService.AbrirCajaAsync(token, 100000, sedeId);

        var venta = await ctx.PosService.RegistrarVentaAsync(token, new RegistrarVentaInput
        {
            Items = [new VentaItem { IdProducto = idProducto, Cantidad = 2 }],
            MetodoPago = "efectivo",
        }, sedeId);

        var detalle = await ctx.VentasService.ObtenerDetalleVentaAsync(token, venta.IdVenta, sedeId);

        Assert.Equal(Fase6Helper.PrecioProteina * 2, detalle.TotalCentavos);
        var item = Assert.Single(detalle.Items);
        Assert.Equal(2, item.Cantidad);
        Assert.Equal("Proteina 1kg", item.DescripcionProducto);
    }

    /// <summary>Vende una membresía real (membresias + membresias_pagos + movimiento).
    /// Requiere caja ya abierta en la sede.</summary>
    private static async Task<string> VenderMembresiaAsync(SecurityTestContext ctx, string token, long sedeId)
    {
        var idSocio = UuidHelper.NewV4();
        await Fase6Helper.InsertarSocioAsync(ctx, idSocio, sedeId);
        var idPlan = await ctx.Planes.InsertAsync(new PlanMembresia
        {
            Nombre = $"Mensual-{UuidHelper.NewV4()[..8]}",
            DiasVigencia = 30,
            DiasCongelamientoMax = 7,
            PrecioCentavos = 10000,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });

        var membresia = await ctx.MembresiasService.VenderAsync(
            token, idSocio, idPlan, "efectivo", 10000, sedeId);
        return membresia.IdMembresia;
    }
}
