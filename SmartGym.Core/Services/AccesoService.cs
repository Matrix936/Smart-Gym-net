using SmartGym.Core.Authorization;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

public sealed class AccesoService : IAccesoService
{
    private readonly IAuthService _auth;
    private readonly IAuthorizationService _authz;
    private readonly IAccesosRepository _accesos;
    private readonly ISedeResolutionService _sedeResolution;
    private readonly ICuentasCobrarRepository _cuentas;
    private readonly IConfiguracionRepository _configuracion;

    public AccesoService(
        IAuthService auth,
        IAuthorizationService authz,
        IAccesosRepository accesos,
        ISedeResolutionService sedeResolution,
        IConfiguracionRepository configuracion,
        ICuentasCobrarRepository cuentas)
    {
        _auth = auth;
        _authz = authz;
        _accesos = accesos;
        _sedeResolution = sedeResolution;
        _configuracion = configuracion;
        _cuentas = cuentas;
    }

    public async Task<AccesoResult> RegistrarAccesoKioskoAsync(
        string idSocio,
        long idSede,
        long? idDispositivo = null,
        CancellationToken ct = default) =>
        // Contexto Kiosco: sin sesión administrativa. La pantalla Kiosco solo
        // expone identificar por huella y registrar acceso (04-integracion-biometrica §10).
        await RegistrarConModoAsync(idSocio, idSede, idDispositivo, AccesoMetodos.Huella, ct);

    public async Task<AccesoResult> RegistrarAccesoManualAsync(
        string token,
        string idSocio,
        long idSede,
        long? idDispositivo = null,
        CancellationToken ct = default)
    {
        // Revalida sesión + permiso en cada operación sensible (access.rs: manual_sin_permiso_falla).
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.AccesoForzarEntradaManual, ct);
        return await RegistrarConModoAsync(idSocio, idSede, idDispositivo, AccesoMetodos.Manual, ct);
    }

    /// <summary>Historial de accesos de la sede (solo lectura). Reutiliza acceso.ver_bitacora.</summary>
    public async Task<PagedResult<AccesoHistorialDto>> BuscarAsync(
        string token,
        AccesoHistorialFiltros? filtros = null,
        int pagina = 1,
        int tamanoPagina = TamanosPagina.Default,
        long? idSedeFrontend = null,
        CancellationToken ct = default)
    {
        var info = await _auth.ValidarSesionAsync(token, ct);
        await _authz.RequierePermisoAsync(token, PermisoCatalogo.AccesoVerBitacora, ct);
        var idSede = await _sedeResolution.ResolverIdSedeOpcionalAsync(info, idSedeFrontend, ct);

        return await _accesos.BuscarAsync(idSede, filtros, pagina, tamanoPagina, ct);
    }

    /// <summary>Lee la modalidad de registro de la BD en CADA llamada (sin caché):
    /// el cambio en Configuración aplica a partir del siguiente toque.</summary>
    private async Task<AccesoResult> RegistrarConModoAsync(
        string idSocio, long idSede, long? idDispositivo, string metodo, CancellationToken ct)
    {
        var modo = AccesoModosRegistro.Normalizar(
            await _configuracion.GetAsync(AccesoModosRegistro.ClaveConfig, ct));

        return metodo == AccesoMetodos.Manual
            ? await _accesos.RegistrarManualAsync(idSocio, idSede, idDispositivo, modo, ct)
            : await _accesos.RegistrarKioskoAsync(idSocio, idSede, idDispositivo, modo, ct);
    }
    /// <summary>Aviso de deuda para el Kiosco: sin sesión (mismo criterio que RegistrarKioskoAsync).</summary>
    public Task<bool> SocioTieneDeudaActivaAsync(string idSocio, CancellationToken ct = default) =>
        _cuentas.SocioTieneDeudaActivaAsync(idSocio, ct);
}