namespace SmartGym.Core.Entities;

/// <summary>empresa_config_fiscal — datos fiscales/conerciales del negocio (fila única).</summary>
public sealed class EmpresaConfigFiscal
{
    public long Id { get; set; }
    public string NombreComercial { get; set; } = string.Empty;
    public string Telefono { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string CodigoPostal { get; set; } = string.Empty;
    public string? RazonSocial { get; set; }
    public string? Rfc { get; set; }
    public string? RegimenFiscal { get; set; }
    public string? LogoPath { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}