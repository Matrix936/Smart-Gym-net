namespace SmartGym.Core.Common;

/// <summary>Fila del listado de cuentas por cobrar con socio/membresía resueltos.</summary>
public sealed class CuentaCobrarDto
{
    public string IdCuenta { get; set; } = string.Empty;
    public string IdSocio { get; set; } = string.Empty;
    public string NombreSocio { get; set; } = string.Empty;
    public string IdMembresia { get; set; } = string.Empty;
    public long SaldoPendienteCentavos { get; set; }
    public string FechaVencimiento { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
}
