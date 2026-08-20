namespace SmartGym.Core.Entities;

/// <summary>socios_historial_estado — trazabilidad de cada cambio de estado del socio.</summary>
public sealed class SocioHistorialEstado
{
    public string Id { get; set; } = string.Empty;
    public string IdSocio { get; set; } = string.Empty;
    public string? EstadoAnterior { get; set; }
    public string EstadoNuevo { get; set; } = string.Empty;
    public string? Motivo { get; set; }
    public long? IdUsuario { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}