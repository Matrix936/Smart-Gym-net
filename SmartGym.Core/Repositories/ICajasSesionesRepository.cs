using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

/// <summary>
/// Sesiones de caja (cash.rs). Abrir/cerrar son atómicos e incluyen su
/// bitácora de auditoría en la misma transacción.
/// </summary>
public interface ICajasSesionesRepository
{
    /// <summary>Apertura atómica: caja + bitácora en una transacción.</summary>
    Task AbrirConBitacoraAsync(CajaSesion sesion, BitacoraAuditoria bitacora, CancellationToken ct = default);

    Task<CajaSesion?> GetByIdAsync(string idSesion, CancellationToken ct = default);

    /// <summary>Sesión abierta de una sede (None si no hay).</summary>
    Task<CajaSesion?> GetAbiertaPorSedeAsync(long idSede, CancellationToken ct = default);

    Task<bool> ExisteAbiertaEnSedeAsync(long idSede, CancellationToken ct = default);

    /// <summary>
    /// Cierre atómico: calcula monto_esperado (inicial + Σ movimientos afecta_efectivo),
    /// actualiza la caja y registra bitácora. Devuelve el monto_esperado computado.
    /// </summary>
    Task<long> CerrarConBitacoraAsync(string idSesion, long montoFinalCentavos, BitacoraAuditoria bitacora, CancellationToken ct = default);
}