using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface ICuentasCobrarRepository
{
    Task<CuentaCobrar?> GetByMembresiaAsync(string idMembresia, CancellationToken ct = default);

    Task<CuentaCobrar?> GetByIdAsync(string idCuenta, CancellationToken ct = default);

    /// <summary>Cuenta originada por una venta POS a crédito (vínculo id_venta); null si no existe.</summary>
    Task<CuentaCobrar?> GetPorVentaAsync(string idVenta, CancellationToken ct = default);

    /// <summary>True si la cuenta tiene al menos un abono (cobros_cuotas) registrado.</summary>
    Task<bool> TieneAbonosAsync(string idCuenta, CancellationToken ct = default);

    /// <summary>
    /// True si el socio tiene alguna cuenta en pendiente/parcial cuya
    /// fecha_vencimiento ya pasó (hoyIsoUtc, ISO). Gate de venta a crédito en POS.
    /// </summary>
    Task<bool> SocioTieneDeudaVencidaAsync(string idSocio, string hoyIsoUtc, CancellationToken ct = default);

/// <summary>
/// True si el socio tiene alguna cuenta pendiente/parcial con saldo &gt; 0
/// (sin importar vencimiento). Aviso de deuda en el Kiosco.
/// </summary>
Task<bool> SocioTieneDeudaActivaAsync(string idSocio, CancellationToken ct = default);

    /// <summary>Listado paginado de cuentas por cobrar de una sede con socio resuelto.</summary>
    Task<PagedResult<CuentaCobrarDto>> BuscarAsync(
        long idSede,
        string? estado,
        string? nombreSocio,
        int pagina,
        int tamanoPagina,
        CancellationToken ct = default);

    /// <summary>Actualiza solo el estado de la cuenta (ej. marcar incobrable). Devuelve filas afectadas.</summary>
    Task<int> CambiarEstadoAsync(string idCuenta, string nuevoEstado, string updatedAt, CancellationToken ct = default);

    /// <summary>
    /// Cobro de abono atómico (finance/cobranza): actualiza saldo y estado de la
    /// cuenta, e inserta cobro + movimiento de caja + bitácora en una transacción.
    /// NotFound si la cuenta no existe o está eliminada.
    /// </summary>
    Task RegistrarAbonoAsync(
        string idCuenta,
        long nuevoSaldoCentavos,
        string nuevoEstado,
        CobroCuota cobro,
        CajaMovimiento movimiento,
        BitacoraAuditoria bitacora,
        CancellationToken ct = default);
}