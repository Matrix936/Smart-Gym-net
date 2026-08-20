using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface IPlanesMembresiaRepository
{
    /// <summary>INSERT y devuelve el id_plan generado (autoincrement).</summary>
    Task<long> InsertAsync(PlanMembresia plan, CancellationToken ct = default);
    Task<PlanMembresia?> GetByIdAsync(long idPlan, CancellationToken ct = default);
    Task<IReadOnlyList<PlanMembresia>> GetActivosAsync(CancellationToken ct = default);
}