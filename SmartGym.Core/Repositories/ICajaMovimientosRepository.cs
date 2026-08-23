using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface ICajaMovimientosRepository
{
    Task InsertAsync(CajaMovimiento movimiento, CancellationToken ct = default);
    Task<IReadOnlyList<CajaMovimiento>> GetBySesionAsync(string idSesion, CancellationToken ct = default);

    /// <summary>Suma neta que afecta efectivo (ingresos positivos, egresos negativos) de una sesión.</summary>
    Task<long> SumarAfectaEfectivoAsync(string idSesion, CancellationToken ct = default);

    /// <summary>
    /// Historial unificado de movimientos de una sede (POS, membresías, abonos)
    /// con socio/vendedor/estado resueltos vía la referencia polimórfica.
    /// Ordenado por fecha descendente.
    /// </summary>
    Task<PagedResult<MovimientoHistorialDto>> BuscarHistorialAsync(
        long? idSede,
        HistorialFiltros? filtros = null,
        int pagina = 1,
        int tamanoPagina = TamanosPagina.Default,
        CancellationToken ct = default);
}