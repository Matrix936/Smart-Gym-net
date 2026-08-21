using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface IUsuariosRepository
{
    Task<long> InsertAsync(Usuario usuario, CancellationToken ct = default);
    Task<Usuario?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<Usuario?> GetByIdAsync(long idUsuario, CancellationToken ct = default);
    Task<IReadOnlyList<Usuario>> GetActivosAsync(CancellationToken ct = default);
}