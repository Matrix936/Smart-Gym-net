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

/// <summary>Origen de la cuenta por cobrar: membresía con pago parcial o venta POS a crédito.</summary>
public static class CuentaCobrarOrigenes
{
    public const string Membresia = "membresia";
    public const string Pos = "pos";
}

/// <summary>cuentas_cobrar — saldo pendiente de una membresía con pago parcial o una venta POS a crédito.</summary>
public sealed class CuentaCobrar
{
    public string IdCuenta { get; set; } = string.Empty;

    /// <summary>Null cuando el origen es una venta POS (no hay membresía asociada).</summary>
    public string? IdMembresia { get; set; }
    public string Origen { get; set; } = CuentaCobrarOrigenes.Membresia;
    public string IdSocio { get; set; } = string.Empty;
    public long SaldoPendienteCentavos { get; set; }
    public string FechaVencimiento { get; set; } = string.Empty;
    public string Estado { get; set; } = CuentaCobrarEstados.Pendiente;
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}
