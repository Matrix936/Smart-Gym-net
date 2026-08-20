using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface IRolesRepository
{
    Task<Rol?> GetByNameAsync(string nombre, CancellationToken ct = default);
    Task<Rol?> GetByIdAsync(long idRol, CancellationToken ct = default);
    Task<long> InsertAsync(Rol rol, CancellationToken ct = default);
}