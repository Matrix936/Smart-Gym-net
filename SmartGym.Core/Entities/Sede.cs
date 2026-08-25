namespace SmartGym.Core.Entities;

/// <summary>sedes — sucursales del gimnasio (catálogo).</summary>
public sealed class Sede
{
    public long IdSede { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? CodigoPostal { get; set; }
    public string? HorarioApertura { get; set; }
    public string? HorarioCierre { get; set; }
    public bool EsActiva { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}