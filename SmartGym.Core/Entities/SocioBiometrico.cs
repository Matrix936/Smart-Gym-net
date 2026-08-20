namespace SmartGym.Core.Entities;

/// <summary>
/// socios_biometricos — local-only (nunca sincroniza). Template gestionado por el
/// sidecar SmartGym.Biometrics.exe. Re-enrolar el mismo dedo marca la anterior
/// es_activa=0 (trazabilidad, no borrado).
/// </summary>
public sealed class SocioBiometrico
{
    public string IdRegistro { get; set; } = string.Empty;
    public string IdSocio { get; set; } = string.Empty;
    public string Dedo { get; set; } = string.Empty;
    public string ArchivoTemplatePath { get; set; } = string.Empty;
    public string? AlgoritmoSdk { get; set; }
    public bool EsActiva { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string? DeletedAt { get; set; }
}