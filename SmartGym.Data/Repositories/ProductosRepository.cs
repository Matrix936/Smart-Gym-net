using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class ProductosRepository : RepositoryBase, IProductosRepository
{
    private const string Select = "SELECT id_producto, codigo_barras, descripcion, precio_venta_centavos, " +
        "costo_promedio_centavos, id_categoria, requiere_inventario, es_activo, updated_at, sincronizado, " +
        "deleted_at FROM productos ";

    public ProductosRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<Producto?> GetByIdAsync(long idProducto, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Producto>(
            new CommandDefinition(
                Select + "WHERE id_producto = @idProducto AND es_activo = 1 AND deleted_at IS NULL",
                new { idProducto }, cancellationToken: ct));
    }

    public async Task<Producto?> GetByIdCualquierEstadoAsync(long idProducto, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Producto>(
            new CommandDefinition(
                Select + "WHERE id_producto = @idProducto AND deleted_at IS NULL",
                new { idProducto }, cancellationToken: ct));
    }

    public async Task<Producto?> GetByCodigoBarrasAsync(string codigoBarras, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Producto>(
            new CommandDefinition(
                // Igualdad exacta contra el índice único idx_productos_codigo_barras.
                // COLLATE BINARY: el código escaneado debe coincidencia literal
                // (un escáner nunca cambia mayúsculas/minúsculas del código real).
                Select + "WHERE codigo_barras = @codigo AND deleted_at IS NULL AND es_activo = 1",
                new { codigo = codigoBarras.Trim() }, cancellationToken: ct));
    }

    public async Task<long> InsertAsync(Producto producto, CancellationToken ct = default)    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO productos (codigo_barras, descripcion, precio_venta_centavos, " +
                "costo_promedio_centavos, id_categoria, requiere_inventario, es_activo, updated_at, sincronizado) " +
                "VALUES (@CodigoBarras, @Descripcion, @PrecioVentaCentavos, @CostoPromedioCentavos, " +
                "@IdCategoria, @RequiereInventario, @EsActivo, @UpdatedAt, 0);",
                producto, cancellationToken: ct));
        return await conn.ExecuteScalarAsync<long>(
            new CommandDefinition("SELECT last_insert_rowid();", cancellationToken: ct));
    }

    private const string SearchWhere =
        "WHERE deleted_at IS NULL " +
        "AND (@query IS NULL OR sin_acentos(descripcion) LIKE '%' || sin_acentos(@query) || '%' COLLATE NOCASE " +
        "OR codigo_barras LIKE '%' || @query || '%' COLLATE NOCASE) " +
        "AND (@esActivo IS NULL OR es_activo = @esActivo) ";

    public async Task<PagedResult<Producto>> SearchAsync(
        string? query,
        int pagina,
        int tamanoPagina,
        bool? esActivo = null,
        CancellationToken ct = default)
    {
        if (!TamanosPagina.EsValido(tamanoPagina))
        {
            throw new ArgumentException($"tamanoPagina inválido: {tamanoPagina}. Valores permitidos: {string.Join(", ", TamanosPagina.Validos)}.", nameof(tamanoPagina));
        }

        var paginaEfectiva = Math.Max(pagina, 1);
        var offset = (paginaEfectiva - 1) * tamanoPagina;
        var queryTrim = string.IsNullOrWhiteSpace(query) ? null : query.Trim();

        await using var conn = ConnectionFactory.Open(DbPath);

        var total = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM productos " + SearchWhere,
                new { query = queryTrim, esActivo }, cancellationToken: ct));

        var rows = await conn.QueryAsync<Producto>(
            new CommandDefinition(
                Select + SearchWhere +
                "ORDER BY descripcion " +
                "LIMIT @tamanoPagina OFFSET @offset",
                new { query = queryTrim, esActivo, tamanoPagina, offset }, cancellationToken: ct));

        return new PagedResult<Producto>
        {
            Items = rows.ToList(),
            TotalRegistros = total,
            Pagina = paginaEfectiva,
            TamanoPagina = tamanoPagina,
        };
    }

    public async Task UpdateAsync(Producto producto, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE productos SET codigo_barras = @CodigoBarras, descripcion = @Descripcion, " +
                "precio_venta_centavos = @PrecioVentaCentavos, requiere_inventario = @RequiereInventario, " +
                "updated_at = @UpdatedAt WHERE id_producto = @IdProducto AND deleted_at IS NULL",
                producto, cancellationToken: ct));
    }

    public async Task DesactivarAsync(long idProducto, string updatedAt, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE productos SET es_activo = 0, updated_at = @updatedAt " +
                "WHERE id_producto = @idProducto AND deleted_at IS NULL",
                new { idProducto, updatedAt }, cancellationToken: ct));
    }

    public async Task ActivarAsync(long idProducto, string updatedAt, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE productos SET es_activo = 1, updated_at = @updatedAt " +
                "WHERE id_producto = @idProducto AND deleted_at IS NULL",
                new { idProducto, updatedAt }, cancellationToken: ct));
    }
}