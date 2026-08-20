using SmartGym.Core.Entities;

namespace SmartGym.Core.Services;

public sealed class AuthResult
{
    public required string Token { get; init; }
    public required SessionInfo Sesion { get; init; }
}

/// <summary>
/// Módulo auth (02-reglas-de-negocio.md §3): login con bcrypt, token solo hasheado
/// en sesiones, mensaje genérico que no revela si el email existe, logout con
/// revoked_at, y cuentas recordadas locales (display, nunca credenciales).
/// </summary>
public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct = default);
    Task LogoutAsync(string token, CancellationToken ct = default);
    /// <summary>Revalida la sesión contra la DB y devuelve IdSede resuelto.</summary>
    Task<SessionInfo> ValidarSesionAsync(string token, CancellationToken ct = default);
    /// <summary>Restaura la sesión persistida (si existe y es válida); null si no hay o caducó.</summary>
    Task<SessionInfo?> RestaurarSesionAsync(CancellationToken ct = default);
    /// <summary>Reautorización con clave (operaciones extra-sensibles) contra password_hash.</summary>
    Task ReautorizarAsync(string token, string password, CancellationToken ct = default);
    Task<IReadOnlyList<CuentaRecordadaLocal>> ListarCuentasRecordadasAsync(CancellationToken ct = default);
}