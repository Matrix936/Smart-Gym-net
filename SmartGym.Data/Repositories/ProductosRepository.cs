using Dapper;
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

    public async Task<long> InsertAsync(Producto producto, CancellationToken ct = default)
    {
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
}