using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

public sealed class CajaService : ICajaService
{
    private readonly IAuthService _auth;
    private readonly IAuthorizationService _authz;
    private readonly ICajasSesionesRepository _cajas;
    private readonly ICajaMovimientosRepository _movimientos;
    private readonly ISedeResolutionService _sedeResolution;
    private readonly IBitacoraAuditoriaRepository _bitacora;

    public CajaService(
        IAuthService auth,
        IAuthorizationService authz,
        ICajasSesionesRepository cajas,
        ICajaMovimientosRepository movimientos,
        ISedeResolutionService sedeResolution,
        IBitacoraAuditoriaRepository bitacora)
    {
        _auth = auth;
        _authz = authz;
        _cajas = cajas;
        _movimientos = movimientos;
        _sedeResolution = sedeResolution;
        _bitacora = bitacora;
    }

    public async Task<CajaSesion> AbrirCajaAsync(
        string token,
        long montoInicialCentavos,
        long? idSedeFrontend = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.CajaAbrir, ct);

        if (montoInicialCentavos < 0)
        {
            throw BusinessException.Validation("El monto inicial no puede ser negativo", "monto_negativo");
        }

        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);

        if (await _cajas.ExisteAbiertaEnSedeAsync(idSede, ct))
        {
            throw BusinessException.Conflict("Ya existe una caja abierta en esta sede", "caja_ya_abierta");
        }

        var ahora = DateHelper.NowIsoUtc();
        var sesion = new CajaSesion
        {
            IdSesion = UuidHelper.NewV4(),
            IdUsuario = info.IdUsuario,
            IdSede = idSede,
            MontoInicialCentavos = montoInicialCentavos,
            FechaApertura = ahora,
            Estado = CajaEstados.Abierta,
            UpdatedAt = ahora,
        };

        await _cajas.AbrirConBitacoraAsync(
            sesion,
            RegistrarBitacora(info, "caja.abierta", sesion.IdSesion, idSede, null, null),
            ct);

        return sesion;
    }

    public async Task<CajaSesion?> ObtenerCajaAbiertaAsync(string token, long? idSede = null, CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);

        long? sede = info.IdSede ?? idSede;
        if (sede is null)
        {
            return null;
        }

        return await _cajas.GetAbiertaPorSedeAsync(sede.Value, ct);
    }

    public async Task<CajaMovimiento> RegistrarMovimientoManualAsync(
        string token,
        string tipo,
        string concepto,
        long montoCentavos,
        string metodoPago,
        long? idSedeFrontend = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.CajaAbrir, ct);

        var tipoTrim = tipo.Trim().ToLowerInvariant();
        if (tipoTrim is not MovimientoTipos.Ingreso and not MovimientoTipos.Egreso)
        {
            throw BusinessException.Validation("Tipo de movimiento inválido", "tipo_movimiento_invalido");
        }

        var conceptoTrim = concepto.Trim();
        if (string.IsNullOrWhiteSpace(conceptoTrim))
        {
            throw BusinessException.Validation("El concepto es requerido", "concepto_requerido");
        }

        if (montoCentavos <= 0)
        {
            throw BusinessException.Validation("El monto debe ser mayor a cero", "monto_invalido");
        }

        var metodoPagoTrim = metodoPago.Trim().ToLowerInvariant();
        if (!MetodosPago.Todos.Contains(metodoPagoTrim))
        {
            throw BusinessException.Validation("Método de pago inválido", "metodo_pago_invalido");
        }

        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);
        var caja = await _cajas.GetAbiertaPorSedeAsync(idSede, ct)
            ?? throw BusinessException.Conflict("No hay caja abierta para registrar el movimiento", "caja_no_abierta");

        var ahora = DateHelper.NowIsoUtc();
        var movimiento = new CajaMovimiento
        {
            IdMovimiento = UuidHelper.NewV4(),
            IdSesion = caja.IdSesion,
            Tipo = tipoTrim,
            Concepto = conceptoTrim,
            MontoCentavos = montoCentavos,
            MetodoPago = metodoPagoTrim,
            AfectaEfectivo = metodoPagoTrim == MetodosPago.Efectivo,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        };

        await _movimientos.InsertAsync(movimiento, ct);
        await _bitacora.InsertAsync(
            RegistrarBitacora(info, $"caja.{tipoTrim}_manual", movimiento.IdMovimiento, idSede, null, $"{conceptoTrim}:{montoCentavos}"),
            ct);

        return movimiento;
    }

    public async Task<CajaSesion> CerrarCajaAsync(
        string token,
        string idSesion,
        long montoFinalContadoCentavos,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.CajaCerrar, ct);

        if (montoFinalContadoCentavos < 0)
        {
            throw BusinessException.Validation("El monto final no puede ser negativo", "monto_negativo");
        }

        var existente = await _cajas.GetByIdAsync(idSesion, ct)
            ?? throw BusinessException.NotFound("Caja no encontrada", "caja_no_encontrada");

        if (existente.Estado != CajaEstados.Abierta)
        {
            throw BusinessException.Conflict("La caja ya está cerrada", "caja_ya_cerrada");
        }

        await _cajas.CerrarConBitacoraAsync(
            idSesion,
            montoFinalContadoCentavos,
            RegistrarBitacora(info, "caja.cerrada", idSesion, existente.IdSede,
                $"inicial:{existente.MontoInicialCentavos}", $"final:{montoFinalContadoCentavos}"),
            ct);

        return (await _cajas.GetByIdAsync(idSesion, ct))!;
    }

    private static BitacoraAuditoria RegistrarBitacora(SessionInfo info, string accion, string idRegistro, long idSede, string? anterior, string? nuevo) =>
        new()
        {
            IdRegistro = UuidHelper.NewV4(),
            IdUsuario = info.IdUsuario,
            Accion = accion,
            TablaAfectada = "cajas_sesiones",
            IdRegistroAfectado = idRegistro,
            ValorAnterior = anterior,
            ValorNuevo = nuevo,
            IdSede = idSede,
            CreatedAt = DateHelper.NowIsoUtc(),
            UpdatedAt = DateHelper.NowIsoUtc(),
        };
}
