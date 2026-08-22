using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface IInventarioSucursalRepository
{
    Task<InventarioSucursal?> GetByProductoSedeAsync(long idProducto, long idSede, CancellationToken ct = default);

    /// <summary>Todas las filas de inventario de una sede (para mostrar stock en catálogo/POS).</summary>
    Task<IReadOnlyList<InventarioSucursal>> GetBySedeAsync(long idSede, CancellationToken ct = default);

    Task InsertAsync(InventarioSucursal inventario, CancellationToken ct = default);

    /// <summary>
    /// Ajuste atómico de stock: suma delta (puede ser negativo). Falla si la
    /// fila no existe o si el resultado quedaría negativo.
    /// </summary>
    Task<bool> AjustarStockAsync(long idProducto, long idSede, long delta, string updatedAt, CancellationToken ct = default);
}