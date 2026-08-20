using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface ICajaMovimientosRepository
{
    Task InsertAsync(CajaMovimiento movimiento, CancellationToken ct = default);
    Task<IReadOnlyList<CajaMovimiento>> GetBySesionAsync(string idSesion, CancellationToken ct = default);

    /// <summary>Suma neta que afecta efectivo (ingresos positivos, egresos negativos) de una sesión.</summary>
    Task<long> SumarAfectaEfectivoAsync(string idSesion, CancellationToken ct = default);
}