namespace SmartGym.Core.Entities;

public static class DispositivoAccesoTipos
{
    public const string Biometrico = "biometrico";
    public const string Manual = "manual";
}

/// <summary>dispositivos_acceso — catálogo de dispositivos de control de acceso por sede (id autoincrement).</summary>
public sealed class DispositivoAcceso
{
    public long IdDispositivo { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = DispositivoAccesoTipos.Biometrico;
    public long IdSede { get; set; }
    public bool EsActivo { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}