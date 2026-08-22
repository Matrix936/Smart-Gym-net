using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface IProductosRepository
{
    /// <summary>Producto activo y no eliminado, o null.</summary>
    Task<Producto?> GetByIdAsync(long idProducto, CancellationToken ct = default);

    /// <summary>Producto sin importar es_activo (para editar/desactivar/activar).</summary>
    Task<Producto?> GetByIdCualquierEstadoAsync(long idProducto, CancellationToken ct = default);

    Task<long> InsertAsync(Producto producto, CancellationToken ct = default);

    /// <summary>Búsqueda paginada por descripción o código de barras.</summary>
    Task<PagedResult<Producto>> SearchAsync(
        string? query,
        int pagina,
        int tamanoPagina,
        bool? esActivo = null,
        CancellationToken ct = default);

    Task UpdateAsync(Producto producto, CancellationToken ct = default);

    /// <summary>Soft-desactivación: es_activo=0, la fila permanece (ventas históricas intactas).</summary>
    Task DesactivarAsync(long idProducto, string updatedAt, CancellationToken ct = default);

    Task ActivarAsync(long idProducto, string updatedAt, CancellationToken ct = default);
}