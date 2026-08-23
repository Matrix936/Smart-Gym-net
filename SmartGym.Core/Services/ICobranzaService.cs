using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Services;

/// <summary>
/// Módulo cobranza (finance): abonos a cuentas por cobrar (caja abierta +
/// movimiento de caja) y registro manual de recordatorios de pago.
/// </summary>
public interface ICobranzaService
{
    /// <summary>Resta saldo, inserta cobro y movimiento de caja. Cobrada al llegar a 0.</summary>
    Task<CuentaCobrar> RegistrarAbonoAsync(
        string token,
        string idCuenta,
        long montoCentavos,
        string metodoPago,
        long? idSedeFrontend = null,
        CancellationToken ct = default);

    /// <summary>Registra el envío de un recordatorio (resultado='enviado').</summary>
    Task<CobroRecordatorio> RegistrarRecordatorioAsync(
        string token,
        string idSocio,
        string tipo,
        CancellationToken ct = default);

    /// <summary>Listado paginado de cuentas por cobrar de la sede con socio resuelto.</summary>
    Task<PagedResult<CuentaCobrarDto>> BuscarAsync(
        string token,
        long idSede,
        string? estado = null,
        string? nombreSocio = null,
        int pagina = 1,
        int tamanoPagina = TamanosPagina.Default,
        CancellationToken ct = default);
}