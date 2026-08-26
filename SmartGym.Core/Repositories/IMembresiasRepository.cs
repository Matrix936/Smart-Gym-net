using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

/// <summary>
/// Membresías (memberships.rs). Vender/congelar/cancelar son atómicos e incluyen
/// su bitácora de auditoría en la misma transacción.
/// </summary>
public interface IMembresiasRepository
{
    Task<Membresia?> GetByIdAsync(string idMembresia, CancellationToken ct = default);
    Task<IReadOnlyList<Membresia>> GetBySocioAsync(string idSocio, CancellationToken ct = default);

    /// <summary>Máxima fecha_fin de las membresías del socio (renovar sin perder días).</summary>
    Task<string?> GetUltimaFechaFinAsync(string idSocio, CancellationToken ct = default);

    /// <summary>
    /// Venta atómica: membresía + pago + movimiento de caja (+ cuenta por cobrar
    /// si pago parcial) + bitácora. `movimiento` es null cuando el dinero ya
    /// entró con la venta POS que originó la membresía (combo_membresia) — en
    /// ese caso NO se inserta movimiento de caja ni cuenta por cobrar.
    /// </summary>
    Task VenderAsync(
        Membresia membresia,
        MembresiaPago pago,
        CajaMovimiento? movimiento,
        CuentaCobrar? cuenta,
        BitacoraAuditoria bitacora,
        CancellationToken ct = default);

    /// <summary>Congelamiento atómico: inserta congelamiento, marca congelada y extiende fecha_fin + bitácora.</summary>
    Task CongelarAsync(
        MembresiaCongelamiento congelamiento,
        string idMembresia,
        string nuevaFechaFin,
        BitacoraAuditoria bitacora,
        CancellationToken ct = default);

    /// <summary>Cancelación atómica: estado=cancelada, fecha_cancelacion=now + bitácora.</summary>
    Task CancelarAsync(
        string idMembresia,
        string fechaCancelacion,
        BitacoraAuditoria bitacora,
        CancellationToken ct = default);

    /// <summary>
    /// Listado paginado de membresías de una sede con socio/plan resueltos y
    /// estado efectivo calculado en SQL (misma semántica que
    /// MembresiaEstadoCalculator: una 'activa' vence por fecha_fin pasada).
    /// </summary>
    Task<PagedResult<MembresiaListadoDto>> BuscarAsync(
        long? idSede,
        string? nombreSocio,
        string? estadoEfectivo,
        long? idPlan,
        int pagina,
        int tamanoPagina,
        CancellationToken ct = default);
}