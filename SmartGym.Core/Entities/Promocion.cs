using SmartGym.Core.Common;
using SmartGym.Core.Services;

namespace SmartGym.Core.Entities;

/// <summary>Tipos de promoción del catálogo.</summary>
public static class PromocionTipos
{
    public const string Descuento = "descuento";
    public const string Combo = "combo";

    /// <summary>Combo que incluye 1 plan de membresía + 1..n productos a precio cerrado. Siempre de contado.</summary>
    public const string ComboMembresia = "combo_membresia";
}

public static class PromocionTiposDescuento
{
    public const string MontoFijo = "monto_fijo";
    public const string Porcentaje = "porcentaje";
}

/// <summary>
/// promociones — descuentos por producto y combos con precio cerrado. Una sola
/// tabla: tipo discrimina (mismo criterio que cuentas_cobrar.origen).
/// FechaInicio/FechaFin son 'yyyy-MM-dd' (date-only) o null.
/// </summary>
public sealed class Promocion
{
    public string IdPromocion { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }

    /// <summary>Solo descuento: producto con precio rebajado. Null en combos.</summary>
    public long? IdProducto { get; set; }

    /// <summary>Solo descuento: monto_fijo | porcentaje.</summary>
    public string? TipoDescuento { get; set; }

    /// <summary>Solo descuento: centavos a descontar, o entero 1..100 si es porcentaje.</summary>
    public long? Valor { get; set; }

    /// <summary>Solo combo: precio cerrado del combo completo.</summary>
    public long? PrecioComboCentavos { get; set; }

    /// <summary>Solo combo_membresia: el plan incluido en el combo.</summary>
    public long? IdPlan { get; set; }

    public string? FechaInicio { get; set; }
    public string? FechaFin { get; set; }
    public bool EsActivo { get; set; } = true;
    public string UpdatedAt { get; set; } = string.Empty;
    public bool Sincronizado { get; set; }
    public string? DeletedAt { get; set; }
}

/// <summary>promocion_productos — componentes de un combo.</summary>
public sealed class PromocionComponente
{
    public string IdPromocion { get; set; } = string.Empty;
    public long IdProducto { get; set; }
    public long Cantidad { get; set; }
}

public sealed class ComponenteInfo
{
    public long IdProducto { get; set; }
    public long Cantidad { get; set; }
    public string DescripcionProducto { get; set; } = string.Empty;

    /// <summary>Precio unitario actual del producto (para mostrar ahorro del combo).</summary>
    public long PrecioVentaCentavos { get; set; }
}

/// <summary>Promoción proyectada para el catálogo administrativo (/promociones).</summary>
public sealed class PromocionInfo
{
    public Promocion Promocion { get; set; } = new();
    public string? DescripcionProducto { get; set; }

    /// <summary>Solo combo_membresia: nombre del plan incluido.</summary>
    public string? NombrePlan { get; set; }

    /// <summary>Componentes resueltos (solo combos).</summary>
    public IReadOnlyList<ComponenteInfo> Componentes { get; set; } = Array.Empty<ComponenteInfo>();

    /// <summary>Suma de precio_venta * cantidad de los componentes del combo.</summary>
    public long SubtotalComponentesCentavos { get; set; }

    /// <summary>Vigencia efectiva al día de hoy — la columna cruda nunca se muta por tiempo.</summary>
    public bool VigenteHoy => PromocionesService.EsVigente(Promocion, DateHelper.TodayIso());
}
