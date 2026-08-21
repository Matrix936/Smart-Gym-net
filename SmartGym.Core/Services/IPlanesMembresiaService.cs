using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Services;

/// <summary>
/// Catálogo de planes de membresía. No existía capa de servicio hasta este
/// bloque — PlanesMembresiaRepository se usaba solo de lectura desde
/// MembresiasService (vender). Crear/editar/desactivar son escrituras
/// administrativas y, como todo el resto del sistema, deben pasar por sesión +
/// permiso, no llamarse directo al repositorio desde la UI.
/// </summary>
public interface IPlanesMembresiaService
{
    /// <summary>Requiere sesión válida; no requiere permiso específico (solo lectura). Mismo patrón que ISociosService.BuscarAsync. EsActivo null → sin filtro por estado.</summary>
    Task<PagedResult<PlanMembresia>> BuscarAsync(string token, string? query = null, int pagina = 1, int tamanoPagina = TamanosPagina.Default, bool? esActivo = null, CancellationToken ct = default);

    Task<PlanMembresia> CrearAsync(
        string token,
        string nombre,
        string? descripcion,
        int diasVigencia,
        int diasCongelamientoMax,
        long precioCentavos,
        CancellationToken ct = default);

    Task<PlanMembresia> EditarAsync(
        string token,
        long idPlan,
        string nombre,
        string? descripcion,
        int diasVigencia,
        int diasCongelamientoMax,
        long precioCentavos,
        CancellationToken ct = default);

    /// <summary>No borra el plan ni afecta membresías ya vendidas con él — solo deja de ofrecerse.</summary>
    Task DesactivarAsync(string token, long idPlan, CancellationToken ct = default);

    /// <summary>Vuelve a ofrecer el plan para venta.</summary>
    Task ActivarAsync(string token, long idPlan, CancellationToken ct = default);
}
