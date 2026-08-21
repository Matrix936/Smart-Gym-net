using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

public sealed class PosService : IPosService
{
    private readonly IAuthService _auth;
    private readonly IAuthorizationService _authz;
    private readonly ISociosRepository _socios;
    private readonly ICajasSesionesRepository _cajas;
    private readonly IProductosRepository _productos;
    private readonly IInventarioSucursalRepository _inventario;
    private readonly IVentasRepository _ventas;
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
            throw BusinessException.Validation("debe haber al menos un item en la venta", "items_vacios");
        }

        var metodoPago = input.MetodoPago.Trim();
        if (metodoPago.Length == 0)
        {
            throw BusinessException.Validation("metodo_pago es obligatorio", "metodo_pago_obligatorio");
        }

        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);
        var caja = await _cajas.GetAbiertaPorSedeAsync(idSede, ct)
            ?? throw BusinessException.Conflict("no hay caja abierta para la sede — abre caja antes de vender", "caja_no_abierta");

        string? idSocio = null;
        if (input.IdSocio is not null)
        {
            idSocio = input.IdSocio.Trim();
            if (idSocio.Length == 0)
            {
                throw BusinessException.Validation("id_socio no puede ser vacio", "id_socio_vacio");
            }

            if (!await _socios.ExistsAsync(idSocio, ct))
            {
                throw BusinessException.NotFound("socio no encontrado", "socio_no_encontrado");
            }
        }

        long totalCentavos = 0;
        var validated = new List<(long idProducto, long cantidad, long precio, bool requiereInventario)>();
        foreach (var item in input.Items)
        {
            if (item.Cantidad <= 0)
            {
                throw BusinessException.Validation(
                    $"cantidad del producto {item.IdProducto} debe ser mayor a 0",
                    "cantidad_invalida");
            }

            var producto = await _productos.GetByIdAsync(item.IdProducto, ct)
                ?? throw BusinessException.NotFound(
                    $"producto {item.IdProducto} no encontrado o inactivo",
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
                    $"stock insuficiente para producto {idProducto}: disponible {stock}, solicitado {cantidad}",
                    "stock_insuficiente");
            }
        }

        var ahora = DateHelper.NowIsoUtc();
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
            MontoCentavos = totalCentavos,
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
            ct);

        return new VentaInfo
        {
            IdVenta = idVenta,
            IdSocio = idSocio,
            IdSede = idSede,
            TotalCentavos = totalCentavos,
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
            ?? throw BusinessException.NotFound("venta no encontrada", "venta_no_encontrada");

        if (venta.Estado == VentaEstados.Cancelada)
        {
            throw BusinessException.Conflict("la venta ya esta cancelada", "venta_ya_cancelada");
        }

        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);
        var caja = await _cajas.GetAbiertaPorSedeAsync(idSede, ct)
            ?? throw BusinessException.Conflict("no hay caja abierta para procesar la devolucion", "caja_no_abierta");

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