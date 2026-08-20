using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface ISedesRepository
{
    Task<Sede?> GetByIdAsync(long idSede, CancellationToken ct = default);
    Task<Sede?> GetPrincipalAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Sede>> GetActivasAsync(CancellationToken ct = default);

    /// <summary>INSERT directo (setup multi-sede y tests). Devuelve el id autoincrement.</summary>
    Task<long> InsertAsync(Sede sede, CancellationToken ct = default);
}