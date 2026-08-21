using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface IPlanesMembresiaRepository
{
    /// <summary>INSERT y devuelve el id_plan generado (autoincrement).</summary>
    Task<long> InsertAsync(PlanMembresia plan, CancellationToken ct = default);
    Task<PlanMembresia?> GetByIdAsync(long idPlan, CancellationToken ct = default);
    Task<IReadOnlyList<PlanMembresia>> GetActivosAsync(CancellationToken ct = default);

    /// <summary>Actualiza los campos editables del plan (no toca es_activo ni deleted_at).</summary>
    Task UpdateAsync(PlanMembresia plan, CancellationToken ct = default);

    /// <summary>Marca el plan como inactivo (es_activo=0) — no es borrado lógico, solo deja de ofrecerse.</summary>
    Task DesactivarAsync(long idPlan, string updatedAt, CancellationToken ct = default);
}