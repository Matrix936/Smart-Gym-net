namespace SmartGym.Core.Services;

/// <summary>
/// Módulo authorization (02-reglas-de-negocio.md §3): catálogo de acciones en
/// código, seed idempotente de permisos_rol para SUPERADMIN en el primer
/// arranque, y requiere_permiso que revalida la sesión en cada operación sensible.
/// </summary>
public interface IAuthorizationService
{
    Task SeedSuperadminPermisosAsync(CancellationToken ct = default);
    /// <summary>Revalida sesión + verifica la acción en permisos_rol. Unauthorized si no aplica.</summary>
    Task RequierePermisoAsync(string token, string accion, CancellationToken ct = default);
    Task<bool> TienePermisoAsync(string token, string accion, CancellationToken ct = default);
}