namespace SmartGym.Core.Entities;

/// <summary>Estados del saldo de una cuenta por cobrar.</summary>
public static class CuentaCobrarEstados
{
    public const string Pendiente = "pendiente";
    public const string Parcial = "parcial";
    public const string Cobrada = "cobrada";
    public const string Incobrable = "incobrable";

    public static IReadOnlyList<string> Validos() => [Pendiente, Parcial, Cobrada, Incobrable];
}

/// <summary>cuentas_cobrar — saldo pendiente de una membresía con pago parcial.</summary>
public sealed class CuentaCobrar
{
    public string IdCuenta { get; set; } = string.Empty;
    public string IdMembresia { get; set; } = string.Empty;
    public string IdSocio { get; set; } = string.Empty;
    public long SaldoPendienteCentavos { get; set; }
    public string FechaVencimiento { get; set; } = string.Empty;
    public string Estado { get; set; } = CuentaCobrarEstados.Pendiente;
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}