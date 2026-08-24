namespace SmartGym.Core.Entities;

public static class VentaEstados
{
    public const string Completada = "completada";
    public const string Cancelada = "cancelada";
}

/// <summary>ventas — cabecera de venta POS.</summary>
public sealed class Venta
{
    public string IdVenta { get; set; } = string.Empty;
    public string? IdSocio { get; set; }
    public long IdSede { get; set; }
    public long TotalCentavos { get; set; }
    public string MetodoPago { get; set; } = string.Empty;
    public string? IdCajaMovimiento { get; set; }
    public long? IdVendedor { get; set; }
    public string Estado { get; set; } = VentaEstados.Completada;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}

/// <summary>detalle_ventas — líneas de una venta (precio server-side).</summary>
public sealed class DetalleVenta
{
    public string IdDetalle { get; set; } = string.Empty;
    public string IdVenta { get; set; } = string.Empty;
    public long IdProducto { get; set; }

    /// <summary>Promoción aplicada a la línea (descuento o combo); null sin promo.</summary>
    public string? IdPromocion { get; set; }
    public long Cantidad { get; set; }
    public long PrecioUnitarioCentavos { get; set; }
    public long SubtotalCentavos { get; set; }
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}

public sealed class VentaItem
{
    public long IdProducto { get; set; }

    /// <summary>
    /// Promoción vendida como unidad (combo). Cuando viene, IdProducto se ignora:
    /// los componentes y el precio salen del catálogo server-side.
    /// </summary>
    public string? IdPromocion { get; set; }
    public long Cantidad { get; set; }
}

public sealed class DetalleVentaInfo
{
    public string IdDetalle { get; set; } = string.Empty;
    public long IdProducto { get; set; }
    public long Cantidad { get; set; }
    public long PrecioUnitarioCentavos { get; set; }
    public long SubtotalCentavos { get; set; }

    /// <summary>Descripción resuelta para UI (null cuando no se consulta el producto).</summary>
    public string? DescripcionProducto { get; set; }
}

public sealed class VentaInfo
{
    public string IdVenta { get; set; } = string.Empty;
    public string? IdSocio { get; set; }
    public long IdSede { get; set; }
    public long TotalCentavos { get; set; }

    /// <summary>Efectivamente pagado en caja (pago completo cuando no hay crédito).</summary>
    public long MontoPagadoCentavos { get; set; }

    /// <summary>Total - monto pagado; 0 salvo ventas a crédito.</summary>
    public long SaldoPendienteCentavos { get; set; }

    /// <summary>
    /// Vencimiento REAL de la cuenta por cobrar creada (ISO), con el plazo que
    /// se usó en esa venta — no un default recalculado en el frontend.
    /// </summary>
    public string? SaldoVenceIsoUtc { get; set; }
    public string MetodoPago { get; set; } = string.Empty;
    public string Estado { get; set; } = VentaEstados.Completada;
    public long? IdVendedor { get; set; }
    public IReadOnlyList<DetalleVentaInfo> Items { get; set; } = Array.Empty<DetalleVentaInfo>();
}

public sealed class RegistrarVentaInput
{
    public IReadOnlyList<VentaItem> Items { get; set; } = Array.Empty<VentaItem>();
    public string? IdSocio { get; set; }
    public string MetodoPago { get; set; } = string.Empty;

    /// <summary>
    /// Monto pagado en caja. Null = pago completo (comportamiento histórico).
    /// Menor al total solo procede con pos.permite_credito encendido y socio válido.
    /// </summary>
    public long? MontoPagadoCentavos { get; set; }

    /// <summary>
    /// Plazo del crédito en días (vencimiento de la cuenta por cobrar).
    /// Null = 15 (default histórico). Solo aplica con pago incompleto.
    /// </summary>
    public int? PlazoCreditoDias { get; set; }
}

public sealed class CancelarVentaInput
{
    public string IdVenta { get; set; } = string.Empty;
    public string PasswordConfirmacion { get; set; } = string.Empty;
}