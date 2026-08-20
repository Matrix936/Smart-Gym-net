using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface IProductosRepository
{
    /// <summary>Producto activo y no eliminado, o null.</summary>
    Task<Producto?> GetByIdAsync(long idProducto, CancellationToken ct = default);

    Task<long> InsertAsync(Producto producto, CancellationToken ct = default);
}