namespace SmartGym.Core.Services;

/// <summary>Sesión resuelta y validada (puebla IdSede para usuarios con sede; null para SUPERADMIN sin sede).</summary>
public sealed class SessionInfo
{
    public required string Token { get; init; }
    public required string IdSesion { get; init; }
    public required long IdUsuario { get; init; }
    public required long IdRol { get; init; }
    public long? IdSede { get; init; }
    public required string Nombre { get; init; }
    public required string Email { get; init; }
}

public interface ISessionState
{
    SessionInfo? Current { get; }
    bool IsAuthenticated { get; }
    void Login(SessionInfo session);
    void Logout();
}

/// <summary>Estado de la sesión actual en memoria (auth.rs: current session state).</summary>
public sealed class SessionState : ISessionState
{
    private SessionInfo? _current;

    public SessionInfo? Current => _current;
    public bool IsAuthenticated => _current is not null;

    public void Login(SessionInfo session) => _current = session;

    public void Logout() => _current = null;
}