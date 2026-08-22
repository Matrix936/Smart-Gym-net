using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

/// <summary>bitacora_auditoria — auditoría transversal (Fase 8 aplica a TODOS los módulos).</summary>
public interface IBitacoraAuditoriaRepository
{
    Task InsertAsync(BitacoraAuditoria registro, CancellationToken ct = default);
    /// <summary>Inspectable para tests: true si NO hay registros de esa acción sobre el registro.</summary>
    Task<bool> NoExisteAccionParaAsync(string tablaAfectada, string idRegistroAfectado, CancellationToken ct = default);

    /// <summary>
    /// Historial de auditoría de una sede con el actor resuelto (JOIN usuarios),
    /// paginado y con filtros de fecha/categoría/acción/usuario. Orden descendente.
    /// </summary>
    Task<PagedResult<BitacoraHistorialDto>> BuscarAsync(
        long idSede,
        BitacoraFiltros? filtros = null,
        int pagina = 1,
        int tamanoPagina = TamanosPagina.Default,
        CancellationToken ct = default);
}