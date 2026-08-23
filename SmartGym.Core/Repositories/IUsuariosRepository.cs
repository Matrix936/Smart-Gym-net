using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface IUsuariosRepository
{
    Task<long> InsertAsync(Usuario usuario, CancellationToken ct = default);
    Task<Usuario?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<Usuario?> GetByIdAsync(long idUsuario, CancellationToken ct = default);
    Task<IReadOnlyList<Usuario>> GetActivosAsync(CancellationToken ct = default);

    /// <summary>Actualiza solo los datos de perfil visibles (nombre/apellidos/email) del propio usuario.</summary>
    Task UpdatePerfilAsync(long idUsuario, string nombre, string apellidoPaterno, string apellidoMaterno,
        string email, string updatedAt, CancellationToken ct = default);

    /// <summary>Actualiza solo el hash de contraseña (cambio voluntario desde Mi Perfil).</summary>
    Task UpdatePasswordAsync(long idUsuario, string passwordHash, string updatedAt, CancellationToken ct = default);
}