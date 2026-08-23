using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

public sealed class MembresiasService : IMembresiasService
{
    private readonly IAuthService _auth;
    private readonly IAuthorizationService _authz;
    private readonly ISociosRepository _socios;
    private readonly IPlanesMembresiaRepository _planes;
    private readonly ICajasSesionesRepository _cajas;
    private readonly IMembresiasRepository _membresias;
    private readonly IBitacoraAuditoriaRepository _bitacora;
    private readonly ISedeResolutionService _sedeResolution;

    public MembresiasService(
        IAuthService auth,
        IAuthorizationService authz,
        ISociosRepository socios,
        IPlanesMembresiaRepository planes,
        ICajasSesionesRepository cajas,
        IMembresiasRepository membresias,
        IBitacoraAuditoriaRepository bitacora,
        ISedeResolutionService sedeResolution)
    {
        _auth = auth;
        _authz = authz;
        _socios = socios;
        _planes = planes;
        _cajas = cajas;
        _membresias = membresias;
        _bitacora = bitacora;
        _sedeResolution = sedeResolution;
    }

    public async Task<PagedResult<MembresiaListadoDto>> BuscarAsync(
        string token,
        string? nombreSocio = null,
        string? estado = null,
        long? idPlan = null,
        int pagina = 1,
        int tamanoPagina = TamanosPagina.Default,
        long? idSedeFrontend = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.MembresiasVer, ct);
        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);

        return await _membresias.BuscarAsync(idSede, nombreSocio, estado, idPlan, pagina, tamanoPagina, ct);
    }

    public async Task<Membresia> VenderAsync(
        string token,
        string idSocio,
        long idPlan,
        string metodoPago,
        long montoRecibidoCentavos,
        long? idSedeFrontend = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.MembresiasCrear, ct);

        if (!await _socios.ExistsAsync(idSocio, ct))
        {
            throw BusinessException.NotFound("Socio no encontrado", "socio_no_encontrado");
        }

        var plan = await _planes.GetByIdAsync(idPlan, ct)
            ?? throw BusinessException.NotFound("Plan no encontrado", "plan_no_encontrado");

        if (!plan.EsActivo)
        {
            throw BusinessException.Validation("El plan no está activo", "plan_inactivo");
        }

        if (montoRecibidoCentavos < 0)
        {
            throw BusinessException.Validation("El monto recibido no puede ser negativo", "monto_invalido");
        }

        if (montoRecibidoCentavos > plan.PrecioCentavos)
        {
            throw BusinessException.Validation("El monto recibido excede el precio del plan", "monto_excesivo");
        }

        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);

        var caja = await _cajas.GetAbiertaPorSedeAsync(idSede, ct)
            ?? throw BusinessException.Conflict("No hay caja abierta en esta sede", "caja_no_abierta");

        // Renovación: no se pierden días — fecha_inicio = max(fecha_fin anterior, hoy).
        var hoy = DateTime.UtcNow;
        var fechaFinPrevia = await _membresias.GetUltimaFechaFinAsync(idSocio, ct);
        var inicio = fechaFinPrevia is null ? hoy : DateHelper.ParseIsoUtc(fechaFinPrevia);
        if (inicio < hoy)
        {
            inicio = hoy;
        }

        var ahora = DateHelper.NowIsoUtc();
        var membresia = new Membresia
        {
            IdMembresia = UuidHelper.NewV4(),
            IdSocio = idSocio,
            IdPlan = idPlan,
            IdSede = idSede,
            FechaInicio = DateHelper.ToIsoUtc(inicio),
            FechaFin = DateHelper.ToIsoUtc(inicio.AddDays(plan.DiasVigencia)),
            Estado = MembresiaEstados.Activa,
            IdVendedor = info.IdUsuario,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        };

        var pago = new MembresiaPago
        {
            IdPago = UuidHelper.NewV4(),
            IdMembresia = membresia.IdMembresia,
            MontoCentavos = montoRecibidoCentavos,
            MetodoPago = metodoPago,
            FechaPago = ahora,
            IdVendedor = info.IdUsuario,
            UpdatedAt = ahora,
        };

        var movimiento = new CajaMovimiento
        {
            IdMovimiento = UuidHelper.NewV4(),
            IdSesion = caja.IdSesion,
            Tipo = MovimientoTipos.Ingreso,
            Concepto = $"Venta de membresía {plan.Nombre}",
            MontoCentavos = montoRecibidoCentavos,
            MetodoPago = metodoPago,
            AfectaEfectivo = true,
            ReferenciaTipo = CajaReferenciaTipos.PagoMembresia,
            ReferenciaId = pago.IdPago,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        };
        pago.IdCajaMovimiento = movimiento.IdMovimiento;

        CuentaCobrar? cuenta = null;
        if (montoRecibidoCentavos < plan.PrecioCentavos)
        {
            cuenta = new CuentaCobrar
            {
                IdCuenta = UuidHelper.NewV4(),
                IdMembresia = membresia.IdMembresia,
                IdSocio = idSocio,
                SaldoPendienteCentavos = plan.PrecioCentavos - montoRecibidoCentavos,
                FechaVencimiento = membresia.FechaFin,
                Estado = montoRecibidoCentavos == 0 ? CuentaCobrarEstados.Pendiente : CuentaCobrarEstados.Parcial,
                UpdatedAt = ahora,
            };
        }

        await _membresias.VenderAsync(
            membresia,
            pago,
            movimiento,
            cuenta,
            RegistrarBitacora(info, "membresia.creada", membresia.IdMembresia, idSede, null, null),
            ct);

        return membresia;
    }

    public async Task<Membresia> CongelarAsync(
        string token,
        string idMembresia,
        string fechaInicio,
        string fechaFin,
        string? motivo = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.MembresiasCongelar, ct);

        var existente = await _membresias.GetByIdAsync(idMembresia, ct)
            ?? throw BusinessException.NotFound("Membresía no encontrada", "membresia_no_encontrada");

        if (existente.Estado != MembresiaEstados.Activa)
        {
            throw BusinessException.Conflict("Solo se puede congelar una membresía activa", "membresia_no_activa");
        }

        var desde = DateHelper.ParseIsoUtc(fechaInicio);
        var hasta = DateHelper.ParseIsoUtc(fechaFin);
        if (hasta <= desde)
        {
            throw BusinessException.Validation("El rango de congelamiento es inválido", "fechas_invalidas");
        }

        var diasCongelacion = (hasta - desde).Days;
        var plan = await _planes.GetByIdAsync(existente.IdPlan, ct)
            ?? throw BusinessException.NotFound("Plan no encontrado", "plan_no_encontrado");

        if (diasCongelacion > plan.DiasCongelamientoMax)
        {
            throw BusinessException.Validation(
                $"El congelamiento excede los días máximos del plan ({plan.DiasCongelamientoMax})",
                "dias_congelamiento_excedido");
        }

        var nuevaFechaFin = DateHelper.ParseIsoUtc(existente.FechaFin).AddDays(diasCongelacion);
        var congelamiento = new MembresiaCongelamiento
        {
            Id = UuidHelper.NewV4(),
            IdMembresia = idMembresia,
            FechaInicio = fechaInicio,
            FechaFin = fechaFin,
            Motivo = motivo,
            AutorizadoPor = info.IdUsuario,
            UpdatedAt = DateHelper.NowIsoUtc(),
        };

        await _membresias.CongelarAsync(
            congelamiento,
            idMembresia,
            DateHelper.ToIsoUtc(nuevaFechaFin),
            RegistrarBitacora(info, "membresia.congelada", idMembresia, existente.IdSede,
                MembresiaEstados.Activa, MembresiaEstados.Congelada),
            ct);

        return (await _membresias.GetByIdAsync(idMembresia, ct))!;
    }

    public async Task<Membresia> CancelarAsync(
        string token,
        string idMembresia,
        string password,
        string? motivo = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.MembresiasCancelar, ct);
        await _auth.ReautorizarAsync(token, password, ct);

        var existente = await _membresias.GetByIdAsync(idMembresia, ct)
            ?? throw BusinessException.NotFound("Membresía no encontrada", "membresia_no_encontrada");

        if (existente.Estado == MembresiaEstados.Cancelada)
        {
            throw BusinessException.Conflict("La membresía ya está cancelada", "membresia_ya_cancelada");
        }

        await _membresias.CancelarAsync(
            idMembresia,
            DateHelper.NowIsoUtc(),
            RegistrarBitacora(info, "membresia.cancelada", idMembresia, existente.IdSede,
                MembresiaEstados.Activa, MembresiaEstados.Cancelada),
            ct);

        return (await _membresias.GetByIdAsync(idMembresia, ct))!;
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
            TablaAfectada = "membresias",
            IdRegistroAfectado = idRegistro,
            ValorAnterior = anterior,
            ValorNuevo = nuevo,
            IdSede = idSede,
            CreatedAt = DateHelper.NowIsoUtc(),
            UpdatedAt = DateHelper.NowIsoUtc(),
        };
}
