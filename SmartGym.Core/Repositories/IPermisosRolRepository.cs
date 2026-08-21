using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

/// <summary>
/// Catálogo de acciones por rol. El seed de SUPERADMIN lo aplica el primer
/// arranque (idempotente) — reemplazo atómico delete+insert en transacción.
/// </summary>
public interface IPermisosRolRepository
{
    Task<IReadOnlyList<PermisoRol>> GetByRolAsync(long idRol, CancellationToken ct = default);
    Task ReplaceAccionesForRolAsync(long idRol, IEnumerable<string> acciones, CancellationToken ct = default);
    Task<bool> TieneFilasAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserta solo las acciones que el rol no tiene (idempotente, no revoca).
    /// Usado por el seed para llevar acciones de catálogos nuevos a bases ya sembradas.
    /// </summary>
    Task AgregarAccionesFaltantesAsync(long idRol, IEnumerable<string> acciones, CancellationToken ct = default);
}