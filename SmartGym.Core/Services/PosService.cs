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
    private readonly IPromocionesRepository _promociones;
    private readonly IPlanesMembresiaRepository _planes;

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
        ISedeResolutionService sedeResolution,
        IPromocionesRepository promociones,
        IPlanesMembresiaRepository planes)
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
        _promociones = promociones;
        _planes = planes;
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
        long? idPlanComboMembresia = null;
        long planShareCentavos = 0;
        var validated = new List<(long idProducto, long cantidad, long precio, bool requiereInventario, string? idPromocion)>();
        foreach (var item in input.Items)
        {
            if (item.Cantidad <= 0)
            {
                throw BusinessException.Validation(
                    $"Cantidad del producto {item.IdProducto} debe ser mayor a 0",
                    "cantidad_invalida");
            }

            if (item.IdPromocion is not null)
            {
                // Combo: precio cerrado server-side; los componentes entran como
                // líneas prorrateadas (sum(detalles) == total se mantiene) y el
                // stock se descuenta por componente dentro de la misma venta.
                var promo = await _promociones.GetByIdAsync(item.IdPromocion, ct)
                    ?? throw BusinessException.NotFound("Promoción no encontrada", "promocion_no_encontrada");

                if (!PromocionesService.EsVigente(promo, DateHelper.NowIsoUtc()))
                {
                    throw BusinessException.Conflict("La promoción no está vigente", "promocion_no_vigente");
                }
                if (promo.Tipo is not (PromocionTipos.Combo or PromocionTipos.ComboMembresia))
                {
                    throw BusinessException.Validation("Solo los combos se venden como promoción", "tipo_promocion_invalido");
                }

                // combo_membresia: el share del plan se separa primero (la UI
                // crea la membresía con ese monto en segunda llamada); el resto
                // del precio cerrado se prorratea entre los productos.
                var esComboMembresia = promo.Tipo == PromocionTipos.ComboMembresia;
                long planSharePorUnidad = 0;
                if (esComboMembresia)
                {
                    var plan = await _planes.GetByIdAsync(promo.IdPlan!.Value, ct)
                        ?? throw BusinessException.NotFound("Plan no encontrado", "plan_no_encontrado");
                    if (!plan.EsActivo)
                    {
                        throw BusinessException.Conflict("El plan del combo no está activo", "plan_inactivo");
                    }

                    var listaProductos = await _promociones.GetComponentesAsync(promo.IdPromocion, ct);
                    long listaProductosCentavos = 0;
                    foreach (var c in listaProductos)
                    {
                        var pLista = await _productos.GetByIdAsync(c.IdProducto, ct);
                        if (pLista is not null)
                        {
                            listaProductosCentavos += pLista.PrecioVentaCentavos * c.Cantidad;
                        }
                    }

                    var listaTotal = plan.PrecioCentavos + listaProductosCentavos;
                    planSharePorUnidad = listaTotal > 0
                        ? (promo.PrecioComboCentavos ?? 0) * plan.PrecioCentavos / listaTotal
                        : 0;
                }

                var componentesRaw = await _promociones.GetComponentesAsync(promo.IdPromocion, ct);
                if (componentesRaw.Count == 0)
                {
                    throw BusinessException.Conflict("El combo no tiene componentes", "combo_sin_componentes");
                }

                var preciosComponentes = new List<(long idProducto, long cantidad, long precioVenta, bool requiereInventario)>();
                long sumaPesos = 0;
                foreach (var c in componentesRaw)
                {
                    var producto = await _productos.GetByIdAsync(c.IdProducto, ct)
                        ?? throw BusinessException.NotFound(
                            $"Producto {c.IdProducto} no encontrado o inactivo",
                            "producto_no_encontrado");
                    if (!producto.EsActivo)
                    {
                        throw BusinessException.Conflict(
                            $"El producto {producto.Descripcion} del combo no está activo",
                            "producto_no_activo_combo");
                    }
                    preciosComponentes.Add((c.IdProducto, c.Cantidad * item.Cantidad,
                        producto.PrecioVentaCentavos, producto.RequiereInventario));
                    sumaPesos += producto.PrecioVentaCentavos * c.Cantidad;
                }

                // Prorrateo del precio cerrado entre componentes proporcional a su
                // precio de venta; el último absorbe el redondeo para que la suma
                // de la línea sea exactamente precioCombo * cantidad. En
                // combo_membresia, el share del plan se separa ANTES y el resto
                // se reparte entre los productos.
                var precioComboTotal = (promo.PrecioComboCentavos ?? 0) * item.Cantidad;
                var planShareTotal = planSharePorUnidad * item.Cantidad;
                var precioProductos = precioComboTotal - planShareTotal;
                var asignado = 0L;
                for (var i = 0; i < preciosComponentes.Count; i++)
                {
                    var comp = preciosComponentes[i];
                    long precioLinea;
                    if (sumaPesos == 0 || i == preciosComponentes.Count - 1)
                    {
                        precioLinea = precioProductos - asignado;
                    }
                    else
                    {
                        precioLinea = precioProductos * (comp.precioVenta * comp.cantidad) / sumaPesos;
                    }
                    asignado += precioLinea;

                    var precioUnitario = comp.cantidad == 0 ? 0 : precioLinea / comp.cantidad;
                    totalCentavos += precioLinea;
                    validated.Add((comp.idProducto, comp.cantidad, precioUnitario,
                        comp.requiereInventario, promo.IdPromocion));
                }

                if (esComboMembresia)
                {
                    idPlanComboMembresia = promo.IdPlan;
                    planShareCentavos = planShareTotal;
                    // El share del plan también se cobra: sin esto el total de la
                    // venta sería solo la parte de productos.
                    totalCentavos += planShareTotal;
                }

                continue;
            }

            var productoIndividual = await _productos.GetByIdAsync(item.IdProducto, ct)
                ?? throw BusinessException.NotFound(
                    $"Producto {item.IdProducto} no encontrado o inactivo",
                    "producto_no_encontrado");

            // Descuento vigente sobre el producto: el precio final lo decide
            // siempre el server (el frontend nunca manda precios).
            string? idPromocionDescuento = null;
            var precioEfectivo = productoIndividual.PrecioVentaCentavos;
            var descuentoVigente = await _promociones.GetDescuentoVigentePorProductoAsync(
                item.IdProducto, DateHelper.NowIsoUtc(), ct);
            if (descuentoVigente is not null)
            {
                precioEfectivo = PromocionesService.CalcularPrecioFinal(precioEfectivo, descuentoVigente);
                idPromocionDescuento = descuentoVigente.IdPromocion;
            }

            var subtotal = precioEfectivo * item.Cantidad;
            totalCentavos += subtotal;
            validated.Add((item.IdProducto, item.Cantidad, precioEfectivo,
                productoIndividual.RequiereInventario, idPromocionDescuento));
        }

        // Stock agregado por producto: un mismo producto puede venir como línea
        // individual y dentro de uno o más combos del mismo carrito.
        var stockRequerido = new Dictionary<long, long>();
        foreach (var v in validated)
        {
            if (!v.requiereInventario)
            {
                continue;
            }
            stockRequerido[v.idProducto] = stockRequerido.GetValueOrDefault(v.idProducto) + v.cantidad;
        }
        foreach (var (idProducto, cantidad) in stockRequerido)
        {
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

        var idVenta = UuidHelper.NewV4();

        CuentaCobrar? cuentaPos = null;
        if (totalPagado < totalCentavos)
        {
            cuentaPos = await ValidarYCrearCuentaCreditoAsync(
                input.IdSocio, idVenta, totalCentavos - totalPagado, totalPagado,
                input.PlazoCreditoDias ?? DiasVencimientoCreditoPos, ahora, ct);
        }

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
            Concepto = "Venta de productos",

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
                IdPromocion = v.idPromocion,
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
            stockRequerido.Select(kv => (kv.Key, kv.Value)).ToList(),
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
            SaldoVenceIsoUtc = cuentaPos?.FechaVencimiento,
            MetodoPago = metodoPago,
            Estado = VentaEstados.Completada,
            IdVendedor = info.IdUsuario,
            IdPlanComboMembresia = idPlanComboMembresia,
            PlanShareCentavos = planShareCentavos,
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

        // combo_membresia: la membresía incluida se gestiona aparte (congelar/
        // cancelar desde /membresías) — cancelar aquí dejaría membresía activa
        // con dinero devuelto solo de productos.
        if (await _promociones.VentaTieneComboMembresiaAsync(venta.IdVenta, ct))
        {
            throw BusinessException.Conflict(
                "No se puede cancelar: esta venta incluyó una membresía. Gestiónala desde /membresías.",
                "venta_mixta_no_cancelable");
        }

        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);
        var caja = await _cajas.GetAbiertaPorSedeAsync(idSede, ct)
            ?? throw BusinessException.Conflict("No hay caja abierta para procesar la devolucion", "caja_no_abierta");

        // Crédito: resolver la cuenta asociada a esta venta. Con abonos no se
        // puede cancelar (dinero ya cobrado que requeriría reversión manual);
        // sin abonos, la cuenta se anula junto con la venta.
        var cuentaVenta = await _cuentas.GetPorVentaAsync(venta.IdVenta, ct);
        if (cuentaVenta is not null && await _cuentas.TieneAbonosAsync(cuentaVenta.IdCuenta, ct))
        {
            throw BusinessException.Conflict(
                "No se puede cancelar: esta venta ya tiene abonos registrados. Contacta al administrador.",
                "venta_con_abonos_no_cancelable");
        }

        var ahora = DateHelper.NowIsoUtc();
        var movimiento = new CajaMovimiento
        {
            IdMovimiento = UuidHelper.NewV4(),
            IdSesion = caja.IdSesion,
            Tipo = MovimientoTipos.Egreso,
            Concepto = "Cancelación de venta",
            MontoCentavos = venta.TotalCentavos,
            MetodoPago = venta.MetodoPago,
            AfectaEfectivo = venta.MetodoPago == "efectivo",
            ReferenciaTipo = CajaReferenciaTipos.CancelacionVenta,
            ReferenciaId = venta.IdVenta,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        };

        BitacoraAuditoria? bitacoraCuenta = null;
        if (cuentaVenta is not null)
        {
            bitacoraCuenta = new BitacoraAuditoria
            {
                IdRegistro = UuidHelper.NewV4(),
                IdUsuario = info.IdUsuario,
                Accion = "cobranza.cuenta_anulada",
                TablaAfectada = "cuentas_cobrar",
                IdRegistroAfectado = cuentaVenta.IdCuenta,
                ValorAnterior = cuentaVenta.Estado,
                ValorNuevo = CuentaCobrarEstados.Anulada,
                IdSede = idSede,
                CreatedAt = ahora,
                UpdatedAt = ahora,
            };
        }

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
            cuentaVenta,
            bitacoraCuenta,
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
    /// el vencimiento usa el plazo recibido de la UI (default 15 días).
    /// </summary>
    private async Task<CuentaCobrar> ValidarYCrearCuentaCreditoAsync(
        string? idSocio, string idVenta, long saldoPendiente, long montoPagado, int plazoDias, string ahora, CancellationToken ct)
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

        if (plazoDias is < 1 or > 180)
        {
            throw BusinessException.Validation(
                "El plazo de crédito debe estar entre 1 y 180 días", "plazo_invalido");
        }

        var hoy = DateHelper.ParseIsoUtc(ahora);
        if (await _cuentas.SocioTieneDeudaVencidaAsync(idSocio, ahora, ct))
        {
            throw BusinessException.Conflict(
                "El socio tiene una deuda vencida — liquida la deuda en Cobranza antes de vender a crédito",
                "socio_tiene_deuda_vencida");
        }

        return new CuentaCobrar
        {
            IdCuenta = UuidHelper.NewV4(),
            IdMembresia = null,
            IdVenta = idVenta,
            Origen = CuentaCobrarOrigenes.Pos,
            IdSocio = idSocio,
            SaldoPendienteCentavos = saldoPendiente,
            FechaVencimiento = DateHelper.ToIsoUtc(hoy.AddDays(plazoDias)),
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