using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

/// <summary>sesiones — tabla local-only (no sincronizable).</summary>
public interface ISesionesRepository
{
    Task InsertAsync(Sesion sesion, CancellationToken ct = default);
    Task<Sesion?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task RevokeAsync(string tokenHash, CancellationToken ct = default);
}