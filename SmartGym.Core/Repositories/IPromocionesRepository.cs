using SmartGym.Core.Common;
using SmartGym.Core.Entities;

namespace SmartGym.Core.Repositories;

public interface IPromocionesRepository
{
    /// <summary>INSERT de la promoción y sus componentes (transaccional). Devuelve el id generado.</summary>
    Task<string> InsertAsync(Promocion promo, IReadOnlyList<PromocionComponente> componentes, CancellationToken ct = default);

    Task<Promocion?> GetByIdAsync(string idPromocion, CancellationToken ct = default);

    /// <summary>Componentes del combo (vacío para descuentos).</summary>
    Task<IReadOnlyList<PromocionComponente>> GetComponentesAsync(string idPromocion, CancellationToken ct = default);

    /// <summary>Búsqueda paginada del catálogo administrativo. Query por nombre/descripción; tipo y esActivo opcionales.</summary>
    Task<PagedResult<Promocion>> SearchAsync(string? query, string? tipo, bool? esActivo, int pagina, int tamanoPagina, CancellationToken ct = default);

    /// <summary>UPDATE de campos editables + reemplazo completo de componentes en la misma transacción.</summary>
    Task UpdateAsync(Promocion promo, IReadOnlyList<PromocionComponente> componentes, CancellationToken ct = default);

    Task SetActivoAsync(string idPromocion, bool activo, string updatedAt, CancellationToken ct = default);

    /// <summary>
    /// Descuento activo (no borrado) sobre el mismo producto cuyo rango de fechas
    /// se solapa con [fechaInicio, fechaFin], excluyendo excluirId (para editar).
    /// Nulls = extremos abiertos. Rango solapa si inicio1 <= fin2 AND inicio2 <= fin1.
    /// </summary>
    Task<Promocion?> GetDescuentoSolapadoAsync(long idProducto, string? fechaInicio, string? fechaFin, string? excluirId, CancellationToken ct = default);

    /// <summary>Descuento activo vigente hoy sobre un producto (para aplicar precio en POS).</summary>
    Task<Promocion?> GetDescuentoVigentePorProductoAsync(long idProducto, string hoy, CancellationToken ct = default);
}
