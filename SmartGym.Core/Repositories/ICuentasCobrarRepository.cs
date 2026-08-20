using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface ICuentasCobrarRepository
{
    Task<CuentaCobrar?> GetByMembresiaAsync(string idMembresia, CancellationToken ct = default);

    Task<CuentaCobrar?> GetByIdAsync(string idCuenta, CancellationToken ct = default);

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