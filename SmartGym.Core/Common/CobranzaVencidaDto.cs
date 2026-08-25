namespace SmartGym.Core.Common;

/// <summary>Cuenta por cobrar VENCIDA (fecha_vencimiento pasada, pendiente/parcial) para el Dashboard.</summary>
public sealed class CobranzaVencidaDto
{
    public string IdCuenta { get; init; } = string.Empty;
    public string IdSocio { get; init; } = string.Empty;
    public string NombreSocio { get; init; } = string.Empty;

    /// <summary>Teléfono del socio (para WhatsApp); vacío si no tiene.</summary>
    public string Telefono { get; init; } = string.Empty;
    public long SaldoPendienteCentavos { get; init; }
    public string FechaVencimiento { get; init; } = string.Empty;

    /// <summary>Días de vencida (positivo).</summary>
    public int DiasVencido { get; init; }
}
