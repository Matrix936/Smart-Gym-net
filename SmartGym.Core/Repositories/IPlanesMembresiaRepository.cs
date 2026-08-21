using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface IPlanesMembresiaRepository
{
    /// <summary>INSERT y devuelve el id_plan generado (autoincrement).</summary>
    Task<long> InsertAsync(PlanMembresia plan, CancellationToken ct = default);
    Task<PlanMembresia?> GetByIdAsync(long idPlan, CancellationToken ct = default);
    Task<IReadOnlyList<PlanMembresia>> GetActivosAsync(CancellationToken ct = default);

    /// <summary>Todos los planes no borrados, activos e inactivos — para el catálogo administrativo.</summary>
    Task<IReadOnlyList<PlanMembresia>> GetTodosAsync(CancellationToken ct = default);

    /// <summary>Búsqueda paginada por nombre/descripción (activos e inactivos) — mismo patrón que SociosRepository.SearchAsync. EsActivo null → sin filtro por estado.</summary>
    Task<PagedResult<PlanMembresia>> SearchAsync(string? query, int pagina, int tamanoPagina, bool? esActivo = null, CancellationToken ct = default);

    /// <summary>Actualiza los campos editables del plan (no toca es_activo ni deleted_at).</summary>
    Task UpdateAsync(PlanMembresia plan, CancellationToken ct = default);

    /// <summary>Marca el plan como inactivo (es_activo=0) — no es borrado lógico, solo deja de ofrecerse.</summary>
    Task DesactivarAsync(long idPlan, string updatedAt, CancellationToken ct = default);

    /// <summary>Marca el plan como activo (es_activo=1) — vuelve a ofrecerse para venta.</summary>
    Task ActivarAsync(long idPlan, string updatedAt, CancellationToken ct = default);
}