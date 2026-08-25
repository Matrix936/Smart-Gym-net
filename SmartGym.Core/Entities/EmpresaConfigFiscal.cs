namespace SmartGym.Core.Entities;

/// <summary>empresa_config_fiscal - datos fiscales/comerciales del negocio (fila única).
/// Teléfono/dirección/CP viven en sedes: son atributos de cada sucursal, no de la empresa.</summary>
public sealed class EmpresaConfigFiscal
{
    public long Id { get; set; }
    public string NombreComercial { get; set; } = string.Empty;
    public string? RazonSocial { get; set; }
    public string? Rfc { get; set; }
    public string? RegimenFiscal { get; set; }
    public string? LogoPath { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}
