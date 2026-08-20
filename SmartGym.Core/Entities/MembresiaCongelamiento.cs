namespace SmartGym.Core.Entities;

/// <summary>membresias_congelamientos — periodo que pausa y extiende fecha_fin de la membresía.</summary>
public sealed class MembresiaCongelamiento
{
    public string Id { get; set; } = string.Empty;
    public string IdMembresia { get; set; } = string.Empty;
    public string FechaInicio { get; set; } = string.Empty;
    public string FechaFin { get; set; } = string.Empty;
    public string? Motivo { get; set; }
    public long? AutorizadoPor { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}