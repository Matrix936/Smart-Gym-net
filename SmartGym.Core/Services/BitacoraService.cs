using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

public interface IBitacoraService
{
    /// <summary>
    /// Historial de auditoría de la sede (solo lectura). El permiso es el
    /// existente acceso.ver_bitacora — no se creó uno nuevo.
    /// </summary>
    Task<PagedResult<BitacoraHistorialDto>> BuscarAsync(
        string token,
        BitacoraFiltros? filtros = null,
        int pagina = 1,
        int tamanoPagina = TamanosPagina.Default,
        long? idSedeFrontend = null,
        CancellationToken ct = default);
}

public sealed class BitacoraService : IBitacoraService
{
    private readonly IAuthService _auth;
    private readonly IAuthorizationService _authz;
    private readonly IBitacoraAuditoriaRepository _bitacora;
    private readonly ISedeResolutionService _sedeResolution;

    public BitacoraService(
        IAuthService auth,
        IAuthorizationService authz,
        IBitacoraAuditoriaRepository bitacora,
        ISedeResolutionService sedeResolution)
    {
        _auth = auth;
        _authz = authz;
        _bitacora = bitacora;
        _sedeResolution = sedeResolution;
    }

    public async Task<PagedResult<BitacoraHistorialDto>> BuscarAsync(
        string token,
        BitacoraFiltros? filtros = null,
        int pagina = 1,
        int tamanoPagina = TamanosPagina.Default,
        long? idSedeFrontend = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.AccesoVerBitacora, ct);
        var idSede = await _sedeResolution.ResolverIdSedeAsync(info, idSedeFrontend, ct);

        return await _bitacora.BuscarAsync(idSede, filtros, pagina, tamanoPagina, ct);
    }
}
