using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Services;

/// <summary>
/// Catálogo de promociones: descuentos por producto y combos. Escrituras
/// administrativas requieren sesión + permiso promociones.gestionar, como
/// todo el resto del sistema.
/// </summary>
public interface IPromocionesService
{
    /// <summary>Solo lectura (sesión válida). tipo null → todos; esActivo null → sin filtro por estado.</summary>
    Task<PagedResult<PromocionInfo>> BuscarAsync(string token, string? query = null, string? tipo = null, bool? esActivo = null, int pagina = 1, int tamanoPagina = TamanosPagina.Default, CancellationToken ct = default);

    Task<PromocionInfo> CrearDescuentoAsync(
        string token,
        string nombre,
        string? descripcion,
        long idProducto,
        string tipoDescuento,
        long valor,
        DateTime? fechaInicio = null,
        DateTime? fechaFin = null,
        CancellationToken ct = default);

    /// <summary>Componentes del combo; el stock se descuenta por componente al vender en POS.</summary>
    Task<PromocionInfo> CrearComboAsync(
        string token,
        string nombre,
        string? descripcion,
        long precioComboCentavos,
        IReadOnlyList<PromocionComponente> componentes,
        DateTime? fechaInicio = null,
        DateTime? fechaFin = null,
        CancellationToken ct = default);

    Task<PromocionInfo> EditarDescuentoAsync(
        string token,
        string idPromocion,
        string nombre,
        string? descripcion,
        long idProducto,
        string tipoDescuento,
        long valor,
        DateTime? fechaInicio = null,
        DateTime? fechaFin = null,
        CancellationToken ct = default);

    Task<PromocionInfo> EditarComboAsync(
        string token,
        string idPromocion,
        string nombre,
        string? descripcion,
        long precioComboCentavos,
        IReadOnlyList<PromocionComponente> componentes,
        DateTime? fechaInicio = null,
        DateTime? fechaFin = null,
        CancellationToken ct = default);

    /// <summary>
    /// Combo que incluye 1 plan de membresía + 1..n productos a precio cerrado.
    /// Siempre de contado al venderse desde POS.
    /// </summary>
    Task<PromocionInfo> CrearComboMembresiaAsync(
        string token,
        string nombre,
        string? descripcion,
        long idPlan,
        long precioComboCentavos,
        IReadOnlyList<PromocionComponente> componentes,
        DateTime? fechaInicio = null,
        DateTime? fechaFin = null,
        CancellationToken ct = default);

    Task<PromocionInfo> EditarComboMembresiaAsync(
        string token,
        string idPromocion,
        string nombre,
        string? descripcion,
        long idPlan,
        long precioComboCentavos,
        IReadOnlyList<PromocionComponente> componentes,
        DateTime? fechaInicio = null,
        DateTime? fechaFin = null,
        CancellationToken ct = default);

    /// <summary>Vuelve a ofrecerse — no revalida solapamiento histórico.</summary>
    Task ActivarAsync(string token, string idPromocion, CancellationToken ct = default);

    /// <summary>Deja de ofrecerse/aplicarse; no borra el historial de ventas.</summary>
    Task DesactivarAsync(string token, string idPromocion, CancellationToken ct = default);

    /// <summary>Catálogo vigente para POS: descuentos activos por producto + combos resueltos.</summary>
    Task<IReadOnlyList<PosPromocionInfo>> ObtenerParaPosAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Catálogo vigente para pantallas públicas sin sesión (Kiosco): misma
    /// proyección y mismo criterio de vigencia efectiva que POS, sin token —
    /// el Kiosco corre sin usuario logueado. Las promociones son globales
    /// (sin IdSede), así que no hay filtro por sede de terminal.
    /// </summary>
    Task<IReadOnlyList<PosPromocionInfo>> ObtenerVigentesParaKioscoAsync(CancellationToken ct = default);
}

public sealed class PosPromocionInfo
{
    public string IdPromocion { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Solo descuento.</summary>
    public long? IdProducto { get; set; }

    /// <summary>Solo descuento: precio final del producto ya con descuento aplicado.</summary>
    public long PrecioFinalCentavos { get; set; }

    /// <summary>Precio original del producto (para tachar en UI). Solo descuento.</summary>
    public long PrecioOriginalCentavos { get; set; }

    /// <summary>Solo combo.</summary>
    public long PrecioComboCentavos { get; set; }


    /// <summary>Solo combo: suma precio_venta * cantidad de componentes (ahorro visible).</summary>
    public long SubtotalComponentesCentavos { get; set; }

    /// <summary>Solo combo_membresia: el plan incluido (para crear la membresía al cobrar).</summary>
    public long? IdPlan { get; set; }
    public string? NombrePlan { get; set; }

    /// <summary>
    /// <summary>Solo combo_membresia: precio de lista total (plan + suma de componentes).
    /// Base del prorrateo del precio cerrado en el momento del cobro.
    /// </summary>
    public long PrecioListaTotalCentavos { get; set; }

    public IReadOnlyList<ComponenteInfo> Componentes { get; set; } = Array.Empty<ComponenteInfo>();
}
