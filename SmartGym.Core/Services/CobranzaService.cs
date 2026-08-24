using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

public sealed class CobranzaService : ICobranzaService
{
    private readonly IAuthService _auth;
    private readonly IAuthorizationService _authz;
    private readonly ISociosRepository _socios;
    private readonly ICajasSesionesRepository _cajas;
    private readonly ICuentasCobrarRepository _cuentas;
    private readonly ICobrosRecordatoriosRepository _recordatorios;
    private readonly IBitacoraAuditoriaRepository _bitacora;
    private readonly ISedeResolutionService _sedeResolution;

    public CobranzaService(
        IAuthService auth,
        IAuthorizationService authz,
        ISociosRepository socios,
        ICajasSesionesRepository cajas,
        ICuentasCobrarRepository cuentas,
        ICobrosRecordatoriosRepository recordatorios,
        IBitacoraAuditoriaRepository bitacora,
        ISedeResolutionService sedeResolution)
    {
        _auth = auth;
        _authz = authz;
        _socios = socios;
        _cajas = cajas;
        _cuentas = cuentas;
        _recordatorios = recordatorios;
        _bitacora = bitacora;
        _sedeResolution = sedeResolution;
    }

    public async Task<CuentaCobrar> RegistrarAbonoAsync(
        string token,
        string idCuenta,
        long montoCentavos,
        string metodoPago,
        long? idSedeFrontend = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.CobranzaRegistrarAbono, ct);

        if (montoCentavos <= 0)
        {
            throw BusinessException.Validation("El monto debe ser mayor a 0", "monto_invalido");
        }

        var metodoPagoTrim = metodoPago.Trim();
        if (metodoPagoTrim.Length == 0)
        {
            throw BusinessException.Validation("Metodo_pago es obligatorio", "metodo_pago_obligatorio");
        }

        var cuenta = await _cuentas.GetByIdAsync(idCuenta, ct)
            ?? throw BusinessException.NotFound("Cuenta no encontrada", "cuenta_no_encontrada");

        if (cuenta.Estado == CuentaCobrarEstados.Cobrada || cuenta.Estado == CuentaCobrarEstados.Incobrable)
        {
            throw BusinessException.Conflict($"La cuenta ya está {cuenta.Estado}", "cuenta_no_activa");
        }

        if (montoCentavos > cuenta.SaldoPendienteCentavos)
        {
            throw BusinessException.Validation("El monto excede el saldo pendiente", "monto_excesivo");
        }

        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);
        var caja = await _cajas.GetAbiertaPorSedeAsync(idSede, ct)
            ?? throw BusinessException.Conflict("No hay caja abierta en esta sede", "caja_no_abierta");

        var nuevoSaldo = cuenta.SaldoPendienteCentavos - montoCentavos;
        var nuevoEstado = nuevoSaldo == 0 ? CuentaCobrarEstados.Cobrada : CuentaCobrarEstados.Parcial;

        var ahora = DateHelper.NowIsoUtc();
        var cobro = new CobroCuota
        {
            IdCobro = UuidHelper.NewV4(),
            IdCuenta = idCuenta,
            MontoCentavos = montoCentavos,
            MetodoPago = metodoPagoTrim,
            FechaCobro = ahora,
            IdCobrador = info.IdUsuario,
            Resultado = CobroCuotaResultados.Exitoso,
            UpdatedAt = ahora,
        };

        var movimiento = new CajaMovimiento
        {
            IdMovimiento = UuidHelper.NewV4(),
            IdSesion = caja.IdSesion,
            Tipo = MovimientoTipos.Ingreso,
            Concepto = "Abono a cuenta",
            MontoCentavos = montoCentavos,
            MetodoPago = metodoPagoTrim,
            AfectaEfectivo = metodoPagoTrim == "efectivo",
            ReferenciaTipo = CajaReferenciaTipos.Abono,
            ReferenciaId = cobro.IdCobro,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        };

        await _cuentas.RegistrarAbonoAsync(
            idCuenta,
            nuevoSaldo,
            nuevoEstado,
            cobro,
            movimiento,
            RegistrarBitacora(info, "cobranza.abono", idCuenta, idSede, cuenta.Estado, nuevoEstado),
            ct);

        return (await _cuentas.GetByIdAsync(idCuenta, ct))!;
    }

    public async Task<CobroRecordatorio> RegistrarRecordatorioAsync(
        string token,
        string idSocio,
        string tipo,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.CobranzaRegistrarAbono, ct);

        if (!CobroRecordatorioTipos.EsValido(tipo))
        {
            throw BusinessException.Validation("Tipo de recordatorio inválido", "tipo_invalido");
        }

        if (!await _socios.ExistsAsync(idSocio, ct))
        {
            throw BusinessException.NotFound("Socio no encontrado", "socio_no_encontrado");
        }

        var ahora = DateHelper.NowIsoUtc();
        var recordatorio = new CobroRecordatorio
        {
            IdRecordatorio = UuidHelper.NewV4(),
            IdSocio = idSocio,
            Tipo = tipo,
            FechaEnvio = ahora,
            Resultado = CobroRecordatorioResultados.Enviado,
            UpdatedAt = ahora,
        };

        await _recordatorios.InsertConBitacoraAsync(
            recordatorio,
            new BitacoraAuditoria
            {
                IdRegistro = UuidHelper.NewV4(),
                IdUsuario = info.IdUsuario,
                Accion = "cobranza.recordatorio",
                TablaAfectada = "cobros_recordatorios",
                IdRegistroAfectado = recordatorio.IdRecordatorio,
                IdSede = info.IdSede,
                CreatedAt = ahora,
                UpdatedAt = ahora,
            },
            ct);

        return recordatorio;
    }

    /// <summary>Marca una cuenta como incobrable (solo pendiente/parcial). Bitácora cobranza.marcada_incobrable.</summary>
    public async Task MarcarIncobrableAsync(string token, string idCuenta, CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.CobranzaRegistrarAbono, ct);

        var cuenta = await _cuentas.GetByIdAsync(idCuenta, ct)
            ?? throw BusinessException.NotFound("Cuenta no encontrada", "cuenta_no_encontrada");

        if (cuenta.Estado == CuentaCobrarEstados.Cobrada || cuenta.Estado == CuentaCobrarEstados.Incobrable)
        {
            throw BusinessException.Conflict($"La cuenta ya está {cuenta.Estado}", "cuenta_no_activa");
        }

        var ahora = DateHelper.NowIsoUtc();
        var anterior = cuenta.Estado;
        await _cuentas.CambiarEstadoAsync(idCuenta, CuentaCobrarEstados.Incobrable, ahora, ct);

        await _bitacora.InsertAsync(new BitacoraAuditoria
        {
            IdRegistro = UuidHelper.NewV4(),
            IdUsuario = info.IdUsuario,
            Accion = "cobranza.marcada_incobrable",
            TablaAfectada = "cuentas_cobrar",
            IdRegistroAfectado = idCuenta,
            ValorAnterior = anterior,
            ValorNuevo = CuentaCobrarEstados.Incobrable,
            IdSede = info.IdSede,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        }, ct);
    }

    public async Task<PagedResult<CuentaCobrarDto>> BuscarAsync(
        string token,
        long idSede,
        string? estado = null,
        string? nombreSocio = null,
        int pagina = 1,
        int tamanoPagina = TamanosPagina.Default,
        CancellationToken ct = default)
    {
        await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.CobranzaRegistrarAbono, ct);

        return await _cuentas.BuscarAsync(idSede, estado, nombreSocio, pagina, tamanoPagina, ct);
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
            TablaAfectada = "cuentas_cobrar",
            IdRegistroAfectado = idRegistro,
            ValorAnterior = anterior,
            ValorNuevo = nuevo,
            IdSede = idSede,
            CreatedAt = DateHelper.NowIsoUtc(),
            UpdatedAt = DateHelper.NowIsoUtc(),
        };
}