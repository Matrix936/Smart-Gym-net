using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface IMembresiasCongelamientosRepository
{
    Task<IReadOnlyList<MembresiaCongelamiento>> GetByMembresiaAsync(string idMembresia, CancellationToken ct = default);
}