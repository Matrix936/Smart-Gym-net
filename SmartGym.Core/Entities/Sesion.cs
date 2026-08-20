namespace SmartGym.Core.Entities;

/// <summary>
/// sesiones — NO sincronizable (03-sincronizacion §2). El token de portador
/// (id_sesion) se le entrega al cliente una sola vez; en la DB solo se guarda
/// el SHA-256 (token_hash). expires_at sugerido: 12h.
/// </summary>
public sealed class Sesion
{
    public string IdSesion { get; set; } = string.Empty;
    public long IdUsuario { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string ExpiresAt { get; set; } = string.Empty;
    public string? RevokedAt { get; set; }
}