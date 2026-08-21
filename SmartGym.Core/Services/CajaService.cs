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
    private readonly ISedeResolutionService _sedeResolution;
    private readonly IBitacoraAuditoriaRepository _bitacora;

    public CajaService(
        IAuthService auth,
        IAuthorizationService authz,
        ICajasSesionesRepository cajas,
        ISedeResolutionService sedeResolution,
        IBitacoraAuditoriaRepository bitacora)
    {
        _auth = auth;
        _authz = authz;
        _cajas = cajas;
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