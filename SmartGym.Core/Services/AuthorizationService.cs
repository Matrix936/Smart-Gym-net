using SmartGym.Core.Authorization;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

public sealed class AuthorizationService : IAuthorizationService
{
    private readonly IAuthService _auth;
    private readonly IRolesRepository _roles;
    private readonly IPermisosRolRepository _permisos;

    public AuthorizationService(IAuthService auth, IRolesRepository roles, IPermisosRolRepository permisos)
    {
        _auth = auth;
        _roles = roles;
        _permisos = permisos;
    }

    /// <summary>
    /// Seed del catálogo completo para SUPERADMIN. Solo corre cuando
    /// permisos_rol está vacía (idempotente; no duplica si ya poblada).
    /// </summary>
    public async Task SeedSuperadminPermisosAsync(CancellationToken ct = default)
    {
        // Sincronización incremental en cada arranque: las bases ya sembradas
        // también deben recibir las acciones que se agreguen al catálogo con el
        // tiempo (el early-return original las dejaba fuera para siempre).
        // Semántica de SUPERADMIN: acceso completo garantizado — una acción del
        // catálogo que se quite manualmente se restaura al arrancar; lo que no
        // se toca son acciones fuera del catálogo ni otros roles.
        var rol = await _roles.GetByNameAsync("SUPERADMIN", ct);
        if (rol is null)
        {
            throw BusinessException.Conflict("El rol SUPERADMIN no existe en el seed", "rol_superadmin_faltante");
        }

        await _permisos.AgregarAccionesFaltantesAsync(rol.IdRol, PermisoCatalogo.Todas(), ct);
    }

    public async Task RequierePermisoAsync(string token, string accion, CancellationToken ct = default)
    {
        // Revalidación server-side de la sesión en cada operación sensible.
        var info = await _auth.ValidarSesionAsync(token, ct);
        var permisos = await _permisos.GetByRolAsync(info.IdRol, ct);

        if (permisos.All(p => !string.Equals(p.Accion, accion, StringComparison.OrdinalIgnoreCase)))
        {
            throw BusinessException.Unauthorized("No autorizado para esta acción", "sin_permiso");
        }
    }

    public async Task<bool> TienePermisoAsync(string token, string accion, CancellationToken ct = default)
    {
        try
        {
            await RequierePermisoAsync(token, accion, ct);
            return true;
        }
        catch (BusinessException ex) when (ex.Error == BusinessError.Unauthorized)
        {
            return false;
        }
    }
}