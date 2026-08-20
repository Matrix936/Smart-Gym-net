namespace SmartGym.Core.Entities;

/// <summary>usuarios — cuentas del personal (id_sede NULL = acceso global/SUPERADMIN).</summary>
public sealed class Usuario
{
    public long IdUsuario { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string ApellidoPaterno { get; set; } = string.Empty;
    public string ApellidoMaterno { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public long IdRol { get; set; }
    public long? IdSede { get; set; }
    public bool EsActivo { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}