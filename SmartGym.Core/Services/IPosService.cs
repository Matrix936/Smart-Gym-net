using SmartGym.Core.Entities;

namespace SmartGym.Core.Services;

/// <summary>
/// Módulo POS (pos.rs): venta de productos e inventario. El total y el precio
/// unitario se calculan server-side; la cancelación exige reautorización con
/// clave y restituye stock + movimiento de caja de egreso.
/// </summary>
public interface IPosService
{
    Task<VentaInfo> RegistrarVentaAsync(
        string token,
        RegistrarVentaInput input,
        long? idSedeFrontend = null,
        CancellationToken ct = default);

    Task CancelarVentaAsync(
        string token,
        CancelarVentaInput input,
        long? idSedeFrontend = null,
        CancellationToken ct = default);
}