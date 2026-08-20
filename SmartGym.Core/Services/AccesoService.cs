using SmartGym.Core.Authorization;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

public sealed class AccesoService : IAccesoService
{
    private readonly IAuthorizationService _authz;
    private readonly IAccesosRepository _accesos;

    public AccesoService(IAuthorizationService authz, IAccesosRepository accesos)
    {
        _authz = authz;
        _accesos = accesos;
    }

    public Task<AccesoResult> RegistrarAccesoKioskoAsync(
        string idSocio,
        long idSede,
        long? idDispositivo = null,
        CancellationToken ct = default) =>
        // Contexto Kiosco: sin sesión administrativa. La pantalla Kiosco solo
        // expone identificar por huella y registrar acceso (04-integracion-biometrica §10).
        _accesos.RegistrarKioskoAsync(idSocio, idSede, idDispositivo, ct);

    public async Task<AccesoResult> RegistrarAccesoManualAsync(
        string token,
        string idSocio,
        long idSede,
        long? idDispositivo = null,
        CancellationToken ct = default)
    {
        // Revalida sesión + permiso en cada operación sensible (access.rs: manual_sin_permiso_falla).
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.AccesoForzarEntradaManual, ct);
        return await _accesos.RegistrarManualAsync(idSocio, idSede, idDispositivo, ct);
    }
}