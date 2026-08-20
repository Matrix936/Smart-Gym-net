using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface IInventarioSucursalRepository
{
    Task<InventarioSucursal?> GetByProductoSedeAsync(long idProducto, long idSede, CancellationToken ct = default);

    Task InsertAsync(InventarioSucursal inventario, CancellationToken ct = default);
}