using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

public sealed class PosService : IPosService
{
    /// <summary>Interruptor maestro de venta a crédito (configuracion_general).</summary>
    private const string ClavePermiteCredito = "pos.permite_credito";

    /// <summary>Vencimiento por defecto de una cuenta por cobrar de venta POS.</summary>
    private const int DiasVencimientoCreditoPos = 15;

    private readonly IAuthService _auth;
    private readonly IAuthorizationService _authz;
    private readonly ISociosRepository _socios;
    private readonly ICajasSesionesRepository _cajas;
    private readonly IProductosRepository _productos;
    private readonly IInventarioSucursalRepository _inventario;
    private readonly IVentasRepository _ventas;
    private readonly ICuentasCobrarRepository _cuentas;
    private readonly IConfiguracionRepository _configuracion;
    private readonly IBitacoraAuditoriaRepository _bitacora;
    private readonly ISedeResolutionService _sedeResolution;

    public PosService(
        IAuthService auth,
        IAuthorizationService authz,
        ISociosRepository socios,
        ICajasSesionesRepository cajas,
        IProductosRepository productos,
        IInventarioSucursalRepository inventario,
        IVentasRepository ventas,
        ICuentasCobrarRepository cuentas,
        IConfiguracionRepository configuracion,
        IBitacoraAuditoriaRepository bitacora,
        ISedeResolutionService sedeResolution)
    {
        _auth = auth;
        _authz = authz;
        _socios = socios;
        _cajas = cajas;
        _productos = productos;
        _inventario = inventario;
        _ventas = ventas;
        _cuentas = cuentas;
        _configuracion = configuracion;
        _bitacora = bitacora;
        _sedeResolution = sedeResolution;
    }

    public async Task<VentaInfo> RegistrarVentaAsync(
        string token,
        RegistrarVentaInput input,
        long? idSedeFrontend = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.PosVender, ct);

        if (input.Items.Count == 0)
        {
            throw BusinessException.Validation("Debe haber al menos un item en la venta", "items_vacios");
        }

        var metodoPago = input.MetodoPago.Trim();
        if (metodoPago.Length == 0)
        {
            throw BusinessException.Validation("Metodo_pago es obligatorio", "metodo_pago_obligatorio");
        }

        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);
        var caja = await _cajas.GetAbiertaPorSedeAsync(idSede, ct)
            ?? throw BusinessException.Conflict("No hay caja abierta para la sede — abre caja antes de vender", "caja_no_abierta");

        string? idSocio = null;
        if (input.IdSocio is not null)
        {
            idSocio = input.IdSocio.Trim();
            if (idSocio.Length == 0)
            {
                throw BusinessException.Validation("Id_socio no puede ser vacio", "id_socio_vacio");
            }

            if (!await _socios.ExistsAsync(idSocio, ct))
            {
                throw BusinessException.NotFound("Socio no encontrado", "socio_no_encontrado");
            }
        }

        long totalCentavos = 0;
        var validated = new List<(long idProducto, long cantidad, long precio, bool requiereInventario)>();
        foreach (var item in input.Items)
        {
            if (item.Cantidad <= 0)
            {
                throw BusinessException.Validation(
                    $"Cantidad del producto {item.IdProducto} debe ser mayor a 0",
                    "cantidad_invalida");
            }

            var producto = await _productos.GetByIdAsync(item.IdProducto, ct)
                ?? throw BusinessException.NotFound(
                    $"Producto {item.IdProducto} no encontrado o inactivo",
                    "producto_no_encontrado");

            var subtotal = producto.PrecioVentaCentavos * item.Cantidad;
            totalCentavos += subtotal;
            validated.Add((item.IdProducto, item.Cantidad, producto.PrecioVentaCentavos, producto.RequiereInventario));
        }

        foreach (var (idProducto, cantidad, _, requiereInventario) in validated)
        {
            if (!requiereInventario)
            {
                continue;
            }

            var stock = (await _inventario.GetByProductoSedeAsync(idProducto, idSede, ct))?.Stock ?? 0;
            if (stock < cantidad)
            {
                throw BusinessException.Conflict(
                    $"Stock insuficiente para producto {idProducto}: disponible {stock}, solicitado {cantidad}",
                    "stock_insuficiente");
            }
        }

        var ahora = DateHelper.NowIsoUtc();

        // Pago parcial = venta a crédito. El total siempre es server-side;
        // monto null se interpreta como pago completo (comportamiento histórico).
        var totalPagado = input.MontoPagadoCentavos ?? totalCentavos;
        if (totalPagado < 0)
        {
            throw BusinessException.Validation("El monto pagado no puede ser negativo", "monto_invalido");
        }
        if (totalPagado > totalCentavos)
        {
            throw BusinessException.Validation("El monto pagado excede el total de la venta", "monto_excesivo");
        }

        CuentaCobrar? cuentaPos = null;
        if (totalPagado < totalCentavos)
        {
            cuentaPos = await ValidarYCrearCuentaCreditoAsync(
                input.IdSocio, totalCentavos - totalPagado, totalPagado, ahora, ct);
        }

        var idVenta = UuidHelper.NewV4();
        var venta = new Venta
        {
            IdVenta = idVenta,
            IdSocio = idSocio,
            IdSede = idSede,
            TotalCentavos = totalCentavos,
            MetodoPago = metodoPago,
            IdVendedor = info.IdUsuario,
            Estado = VentaEstados.Completada,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        };

        var movimiento = new CajaMovimiento
        {
            IdMovimiento = UuidHelper.NewV4(),
            IdSesion = caja.IdSesion,
            Tipo = MovimientoTipos.Ingreso,
            Concepto = "venta",

            // A caja solo entra lo efectivamente pagado; el resto queda en cuentas_cobrar.
            MontoCentavos = totalPagado,
            MetodoPago = metodoPago,
            AfectaEfectivo = metodoPago == "efectivo",
            ReferenciaTipo = CajaReferenciaTipos.Venta,
            ReferenciaId = idVenta,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        };
        venta.IdCajaMovimiento = movimiento.IdMovimiento;

        var detalles = validated
            .Select(v => new DetalleVenta
            {
                IdDetalle = UuidHelper.NewV4(),
                IdVenta = idVenta,
                IdProducto = v.idProducto,
                Cantidad = v.cantidad,
                PrecioUnitarioCentavos = v.precio,
                SubtotalCentavos = v.precio * v.cantidad,
                UpdatedAt = ahora,
            })
            .ToList();

        await _ventas.InsertarCompletaAsync(
            venta,
            movimiento,
            detalles,
            validated.Where(v => v.requiereInventario).Select(v => (v.idProducto, v.cantidad)).ToList(),
            RegistrarBitacora(info, "venta.creada", idVenta, idSede, null, null),
            cuentaPos,
            ct);

        return new VentaInfo
        {
            IdVenta = idVenta,
            IdSocio = idSocio,
            IdSede = idSede,
            TotalCentavos = totalCentavos,
            MontoPagadoCentavos = totalPagado,
            SaldoPendienteCentavos = totalCentavos - totalPagado,
            MetodoPago = metodoPago,
            Estado = VentaEstados.Completada,
            IdVendedor = info.IdUsuario,
            Items = detalles.Select(d => new DetalleVentaInfo
            {
                IdDetalle = d.IdDetalle,
                IdProducto = d.IdProducto,
                Cantidad = d.Cantidad,
                PrecioUnitarioCentavos = d.PrecioUnitarioCentavos,
                SubtotalCentavos = d.SubtotalCentavos,
            }).ToList(),
        };
    }

    public async Task CancelarVentaAsync(
        string token,
        CancelarVentaInput input,
        long? idSedeFrontend = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.PosCancelarVenta, ct);
        await _auth.ReautorizarAsync(token, input.PasswordConfirmacion, ct);

        var venta = await _ventas.GetByIdAsync(input.IdVenta, ct)
            ?? throw BusinessException.NotFound("Venta no encontrada", "venta_no_encontrada");

        if (venta.Estado == VentaEstados.Cancelada)
        {
            throw BusinessException.Conflict("La venta ya esta cancelada", "venta_ya_cancelada");
        }

        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);
        var caja = await _cajas.GetAbiertaPorSedeAsync(idSede, ct)
            ?? throw BusinessException.Conflict("No hay caja abierta para procesar la devolucion", "caja_no_abierta");

        var ahora = DateHelper.NowIsoUtc();
        var movimiento = new CajaMovimiento
        {
            IdMovimiento = UuidHelper.NewV4(),
            IdSesion = caja.IdSesion,
            Tipo = MovimientoTipos.Egreso,
            Concepto = "cancelacion_venta",
            MontoCentavos = venta.TotalCentavos,
            MetodoPago = venta.MetodoPago,
            AfectaEfectivo = venta.MetodoPago == "efectivo",
            ReferenciaTipo = CajaReferenciaTipos.CancelacionVenta,
            ReferenciaId = venta.IdVenta,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        };

        await _ventas.CancelarCompletaAsync(
            venta.IdVenta,
            venta.IdSede,
            movimiento,
            RegistrarBitacora(
                info,
                "venta.cancelada",
                venta.IdVenta,
                venta.IdSede,
                VentaEstados.Completada,
                VentaEstados.Cancelada),
            ct);
    }

    /// <summary>Interruptor pos.permite_credito para la UI (exige sesión válida; lectura no sensible).</summary>
    public async Task<bool> ObtenerPermiteCreditoAsync(string token, CancellationToken ct = default)
    {
        await _auth.ValidarSesionAsync(token, ct);
        return await LeerPermiteCreditoAsync(ct);
    }

    private async Task<bool> LeerPermiteCreditoAsync(CancellationToken ct) =>
        string.Equals(
            await _configuracion.GetAsync(ClavePermiteCredito, ct), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gate de venta a crédito: interruptor global encendido, socio obligatorio
    /// y sin deudas vencidas (pendiente/parcial con fecha_vencimiento pasada).
    /// Crea la cuenta por cobrar igual que MembresiasService.VenderAsync —
    /// vencimiento por defecto a 15 días por no haber fecha fin de membresía.
    /// </summary>
    private async Task<CuentaCobrar> ValidarYCrearCuentaCreditoAsync(
        string? idSocio, long saldoPendiente, long montoPagado, string ahora, CancellationToken ct)
    {
        if (!await LeerPermiteCreditoAsync(ct))
        {
            throw BusinessException.Conflict(
                "La venta con pago incompleto no está permitida — habilita el crédito en Configuración",
                "pago_incompleto_no_permitido");
        }

        if (string.IsNullOrWhiteSpace(idSocio))
        {
            throw BusinessException.Validation(
                "Una venta a crédito requiere un socio asociado", "socio_requerido_credito");
        }

        var hoy = DateHelper.ParseIsoUtc(ahora);
        if (await _cuentas.SocioTieneDeudaVencidaAsync(idSocio, ahora, ct))
        {
            throw BusinessException.Conflict(
                "El socio tiene una deuda vencida — registra un abono en Cobranza antes de vender a crédito",
                "socio_tiene_deuda_vencida");
        }

        return new CuentaCobrar
        {
            IdCuenta = UuidHelper.NewV4(),
            IdMembresia = null,
            Origen = CuentaCobrarOrigenes.Pos,
            IdSocio = idSocio,
            SaldoPendienteCentavos = saldoPendiente,
            FechaVencimiento = DateHelper.ToIsoUtc(hoy.AddDays(DiasVencimientoCreditoPos)),
            Estado = montoPagado == 0 ? CuentaCobrarEstados.Pendiente : CuentaCobrarEstados.Parcial,
            UpdatedAt = ahora,
        };
    }

    private static BitacoraAuditoria RegistrarBitacora(
        SessionInfo info,
        string accion,
        string idRegistro,
        long idSede,
        string? anterior,
        string? nuevo) =>
        new()
        {
            IdRegistro = UuidHelper.NewV4(),
            IdUsuario = info.IdUsuario,
            Accion = accion,
            TablaAfectada = "ventas",
            IdRegistroAfectado = idRegistro,
            ValorAnterior = anterior,
            ValorNuevo = nuevo,
            IdSede = idSede,
            CreatedAt = DateHelper.NowIsoUtc(),
            UpdatedAt = DateHelper.NowIsoUtc(),
        };
}