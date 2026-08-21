using SmartGym.Core.Entities;

namespace SmartGym.Core.Services;

/// <summary>
/// Módulo cash (cash.rs): apertura y cierre de caja. El monto_esperado se
/// calcula server-side (inicial + movimientos que afectan efectivo).
/// </summary>
public interface ICajaService
{
    Task<CajaSesion> AbrirCajaAsync(string token, long montoInicialCentavos, long? idSedeFrontend = null, CancellationToken ct = default);

    /// <summary>Sesión abierta de la sede resuelta (verificar en UI antes de vender).</summary>
    Task<CajaSesion?> ObtenerCajaAbiertaAsync(string token, long? idSede = null, CancellationToken ct = default);

    Task<CajaMovimiento> RegistrarMovimientoManualAsync(
        string token,
        string tipo,
        string concepto,
        long montoCentavos,
        string metodoPago,
        long? idSedeFrontend = null,
        CancellationToken ct = default);

    Task<CajaSesion> CerrarCajaAsync(string token, string idSesion, long montoFinalContadoCentavos, CancellationToken ct = default);
}
