using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Data.Db;
using SmartGym.Tests.Fase4;
using SmartGym.Tests.Security;

namespace SmartGym.Tests.Fase6;

/// <summary>Helpers compartidos por pos.rs portado (13 tests).</summary>
internal static class Fase6Helper
{
    public const long PrecioProteina = 50000;

    /// <summary>Superadmin + producto "Proteina 1kg" (50000, requiere inventario) con stock 10 en la sede.</summary>
    public static async Task<(SecurityTestContext ctx, string token, long sedeId, long idProducto)> BaseAsync()
    {
        var (ctx, token, sedeId) = await Fase4Helper.SuperadminAsync();
        var idProducto = await InsertarProductoAsync(ctx, "Proteina 1kg", PrecioProteina);
        await InsertarInventarioAsync(ctx, idProducto, sedeId, 10);
        return (ctx, token, sedeId, idProducto);
    }

    public static Task<long> InsertarProductoAsync(
        SecurityTestContext ctx,
        string descripcion,
        long precioCentavos,
        bool requiereInventario = true) =>
        ctx.Productos.InsertAsync(new Producto
        {
            Descripcion = descripcion,
            PrecioVentaCentavos = precioCentavos,
            RequiereInventario = requiereInventario,
            EsActivo = true,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });

    public static Task InsertarInventarioAsync(SecurityTestContext ctx, long idProducto, long idSede, long stock) =>
        ctx.Inventario.InsertAsync(new InventarioSucursal
        {
            IdProducto = idProducto,
            IdSede = idSede,
            Stock = stock,
            StockMinimo = 0,
            UpdatedAt = DateHelper.NowIsoUtc(),
        });

    public static Task InsertarSocioAsync(SecurityTestContext ctx, string idSocio, long idSede)
    {
        var ahora = DateHelper.NowIsoUtc();
        return ctx.Socios.InsertAsync(new Socio
        {
            IdSocio = idSocio,
            Nombre = "Juan",
            ApellidoPaterno = string.Empty,
            ApellidoMaterno = string.Empty,
            IdSedeRegistro = idSede,
            Estado = SocioEstados.Activo,
            CreatedAt = ahora,
            UpdatedAt = ahora,
        });
    }

    public static async Task<long> StockAsync(SecurityTestContext ctx, long idProducto, long idSede)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT stock FROM inventario_sucursal WHERE id_producto = @idProducto AND id_sede = @idSede",
            new { idProducto, idSede }));
    }

    public static async Task<string> EstadoVentaAsync(SecurityTestContext ctx, string idVenta)
    {
        await using var conn = ConnectionFactory.Open(ctx.DbPath);
        return await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT estado FROM ventas WHERE id_venta = @idVenta",
            new { idVenta })) ?? string.Empty;
    }
}