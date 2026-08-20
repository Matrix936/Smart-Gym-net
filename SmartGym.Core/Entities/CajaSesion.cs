namespace SmartGym.Core.Entities;

public static class CajaEstados
{
    public const string Abierta = "abierta";
    public const string Cerrada = "cerrada";
}

/// <summary>cajas_sesiones — sesión de caja (id UUID v4). Montos INTEGER centavos.</summary>
public sealed class CajaSesion
{
    public string IdSesion { get; set; } = string.Empty;
    public long IdUsuario { get; set; }
    public long IdSede { get; set; }
    public long MontoInicialCentavos { get; set; }
    public long? MontoFinalCentavos { get; set; }
    public long? MontoEsperadoCentavos { get; set; }
    public string FechaApertura { get; set; } = string.Empty;
    public string? FechaCierre { get; set; }
    public string Estado { get; set; } = CajaEstados.Abierta;
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}