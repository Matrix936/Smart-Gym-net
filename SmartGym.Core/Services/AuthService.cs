using BCrypt.Net;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;

namespace SmartGym.Core.Services;

public sealed class AuthService : IAuthService
{
    private const double SesionHoras = 12;

    private readonly IUsuariosRepository _usuarios;
    private readonly ISesionesRepository _sesiones;
    private readonly ICuentasRecordadasRepository _cuentasRecordadas;
    private readonly ISessionState _sessionState;
    private readonly ISesionStore _sesionStore;

    public AuthService(
        IUsuariosRepository usuarios,
        ISesionesRepository sesiones,
        ICuentasRecordadasRepository cuentasRecordadas,
        ISessionState sessionState,
        ISesionStore sesionStore)
    {
        _usuarios = usuarios;
        _sesiones = sesiones;
        _cuentasRecordadas = cuentasRecordadas;
        _sessionState = sessionState;
        _sesionStore = sesionStore;
    }

    public static string HashPassword(string password) => BCrypt.Net.BCrypt.HashPassword(password);
    public static bool VerifyPassword(string password, string passwordHash) => BCrypt.Net.BCrypt.Verify(password, passwordHash);

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        Usuario? usuario;
        try
        {
            usuario = await _usuarios.GetByEmailAsync(email.Trim(), ct);
        }
        catch
        {
            throw ErrorCredencialesInvalidas();
        }

        // No revelar si el email existe ni por qué falló: mismo error genérico.
        if (usuario is null)
        {
            throw ErrorCredencialesInvalidas();
        }

        if (!VerifyPassword(password, usuario.PasswordHash) || !usuario.EsActivo)
        {
            throw ErrorCredencialesInvalidas();
        }

        var token = UuidHelper.NewV4();
        var now = DateHelper.NowIsoUtc();
        var sesion = new Sesion
        {
            IdSesion = token,
            IdUsuario = usuario.IdUsuario,
            TokenHash = SecurityHelper.HashToken(token),
            CreatedAt = now,
            ExpiresAt = DateHelper.ExpiresAtUtc(SesionHoras),
        };
        await _sesiones.InsertAsync(sesion, ct);

        await _cuentasRecordadas.UpsertAsync(new CuentaRecordadaLocal
        {
            IdUsuario = usuario.IdUsuario,
            Nombre = NombreCompleto(usuario),
            Email = usuario.Email,
            UltimoLogin = now,
        }, ct);

        var info = MapearSesion(usuario, token, sesion);
        _sessionState.Login(info);
        await _sesionStore.GuardarAsync(token);

        return new AuthResult { Token = token, Sesion = info };
    }

    public async Task LogoutAsync(string token, CancellationToken ct = default)
    {
        await _sesiones.RevokeAsync(SecurityHelper.HashToken(token), ct);
        _sessionState.Logout();
        await _sesionStore.LimpiarAsync();
    }

    public async Task<SessionInfo> ValidarSesionAsync(string token, CancellationToken ct = default)
    {
        var tokenHash = SecurityHelper.HashToken(token);
        var sesion = await _sesiones.GetByTokenHashAsync(tokenHash, ct);
        if (sesion is null || !string.IsNullOrEmpty(sesion.RevokedAt))
        {
            throw BusinessException.Unauthorized("Sesión inválida", "sesion_invalida");
        }

        if (DateHelper.ParseIsoUtc(sesion.ExpiresAt) < DateTime.UtcNow)
        {
            throw BusinessException.Unauthorized("Sesión expirada", "sesion_expirada");
        }

        var usuario = await _usuarios.GetByIdAsync(sesion.IdUsuario, ct);
        if (usuario is null || !usuario.EsActivo)
        {
            throw BusinessException.Unauthorized("Sesión inválida", "sesion_invalida");
        }

        return MapearSesion(usuario, token, sesion);
    }

    public async Task<SessionInfo?> RestaurarSesionAsync(CancellationToken ct = default)
    {
        var token = await _sesionStore.ObtenerTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        try
        {
            var info = await ValidarSesionAsync(token, ct);
            _sessionState.Login(info);
            return info;
        }
        catch
        {
            await _sesionStore.LimpiarAsync();
            return null;
        }
    }

    public async Task ReautorizarAsync(string token, string password, CancellationToken ct = default)
    {
        var info = await ValidarSesionAsync(token, ct);
        var usuario = await _usuarios.GetByIdAsync(info.IdUsuario, ct)
            ?? throw BusinessException.Unauthorized("Usuario no encontrado", "sesion_invalida");

        if (!VerifyPassword(password, usuario.PasswordHash))
        {
            throw BusinessException.Unauthorized("Clave incorrecta", "clave_incorrecta");
        }
    }

    public async Task<IReadOnlyList<CuentaRecordadaLocal>> ListarCuentasRecordadasAsync(CancellationToken ct = default) =>
        await _cuentasRecordadas.GetAllAsync(ct);

    private static SessionInfo MapearSesion(Usuario usuario, string token, Sesion sesion) => new()
    {
        Token = token,
        IdSesion = sesion.IdSesion,
        IdUsuario = usuario.IdUsuario,
        IdRol = usuario.IdRol,
        IdSede = usuario.IdSede,
        Nombre = NombreCompleto(usuario),
        Email = usuario.Email,
    };

    /// <summary>Nombre completo igual que Rust (une todas las partes sin espacios extra).</summary>
    private static string NombreCompleto(Usuario usuario) => string.Join(" ",
        new[] { usuario.Nombre, usuario.ApellidoPaterno, usuario.ApellidoMaterno }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    private static BusinessException ErrorCredencialesInvalidas() =>
        BusinessException.Unauthorized("Credenciales inválidas", "credenciales_invalidas");
}