using Dapper;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class InventarioSucursalRepository : RepositoryBase, IInventarioSucursalRepository
{
    private const string Select = "SELECT id_producto, id_sede, stock, stock_minimo, updated_at, " +
        "sincronizado, deleted_at FROM inventario_sucursal ";

    public InventarioSucursalRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<InventarioSucursal?> GetByProductoSedeAsync(long idProducto, long idSede, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<InventarioSucursal>(
            new CommandDefinition(
                Select + "WHERE id_producto = @idProducto AND id_sede = @idSede AND deleted_at IS NULL",
                new { idProducto, idSede }, cancellationToken: ct));
    }

    public async Task InsertAsync(InventarioSucursal inventario, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO inventario_sucursal (id_producto, id_sede, stock, stock_minimo, updated_at, sincronizado) " +
                "VALUES (@IdProducto, @IdSede, @Stock, @StockMinimo, @UpdatedAt, 0);",
                inventario, cancellationToken: ct));
    }
}