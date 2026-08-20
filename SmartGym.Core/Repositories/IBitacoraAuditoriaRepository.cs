using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

/// <summary>bitacora_auditoria — auditoría transversal (Fase 8 aplica a TODOS los módulos).</summary>
public interface IBitacoraAuditoriaRepository
{
    Task InsertAsync(BitacoraAuditoria registro, CancellationToken ct = default);
    /// <summary>Inspectable para tests: true si NO hay registros de esa acción sobre el registro.</summary>
    Task<bool> NoExisteAccionParaAsync(string tablaAfectada, string idRegistroAfectado, CancellationToken ct = default);
}