namespace SmartGym.Core.Entities;

/// <summary>Resultados de intentos de cobro (default 'pendiente').</summary>
public static class CobroCuotaResultados
{
    public const string Exitoso = "exitoso";
    public const string Rechazado = "rechazado";
    public const string Pendiente = "pendiente";
}

/// <summary>cobros_cuotas — cobro/abono aplicado a una cuenta por cobrar.</summary>
public sealed class CobroCuota
{
    public string IdCobro { get; set; } = string.Empty;
    public string IdCuenta { get; set; } = string.Empty;
    public long MontoCentavos { get; set; }
    public string MetodoPago { get; set; } = string.Empty;
    public string FechaCobro { get; set; } = string.Empty;
    public long? IdCobrador { get; set; }
    public string Resultado { get; set; } = CobroCuotaResultados.Exitoso;
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}