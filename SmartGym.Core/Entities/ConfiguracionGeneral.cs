namespace SmartGym.Core.Entities;

/// <summary>configuracion_general — pares clave/valor sincronizables.</summary>
public sealed class ConfiguracionGeneral
{
    public string Clave { get; set; } = string.Empty;
    public string? Valor { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
}