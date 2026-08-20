using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface IMembresiasPagosRepository
{
    Task<IReadOnlyList<MembresiaPago>> GetByMembresiaAsync(string idMembresia, CancellationToken ct = default);
}