using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class PromocionesRepository : RepositoryBase, IPromocionesRepository
{
    private const string Select = "SELECT id_promocion, tipo, nombre, descripcion, id_producto, id_plan, " +
        "tipo_descuento, valor, precio_combo_centavos, fecha_inicio, fecha_fin, es_activo, " +
        "updated_at, sincronizado, deleted_at FROM promociones ";

    private const string SearchWhere =
        "WHERE deleted_at IS NULL " +
        "AND (@query IS NULL OR sin_acentos(nombre) LIKE '%' || sin_acentos(@query) || '%' COLLATE NOCASE " +
        "OR sin_acentos(descripcion) LIKE '%' || sin_acentos(@query) || '%' COLLATE NOCASE) " +
        "AND (@tipo IS NULL OR tipo = @tipo) " +
        "AND (@esActivo IS NULL OR es_activo = @esActivo) ";

    public PromocionesRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<string> InsertAsync(Promocion promo, IReadOnlyList<PromocionComponente> componentes, CancellationToken ct = default)
    {
        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
            await conn.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO promociones (id_promocion, tipo, nombre, descripcion, id_producto, id_plan, " +
                    "tipo_descuento, valor, precio_combo_centavos, fecha_inicio, fecha_fin, es_activo, " +
                    "updated_at, sincronizado) " +
                    "VALUES (@IdPromocion, @Tipo, @Nombre, @Descripcion, @IdProducto, @IdPlan, " +
                    "@TipoDescuento, @Valor, @PrecioComboCentavos, @FechaInicio, @FechaFin, @EsActivo, " +
                    "@UpdatedAt, 0);",
                    promo, tx, cancellationToken: ct));

            await InsertarComponentesCoreAsync(conn, tx, promo.IdPromocion, componentes, ct);
        }, ct);
        return promo.IdPromocion;
    }

    public async Task<Promocion?> GetByIdAsync(string idPromocion, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Promocion>(
            new CommandDefinition(
                Select + "WHERE id_promocion = @idPromocion AND deleted_at IS NULL",
                new { idPromocion }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PromocionComponente>> GetComponentesAsync(string idPromocion, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<PromocionComponente>(
            new CommandDefinition(
                "SELECT id_promocion, id_producto, cantidad FROM promocion_productos " +
                "WHERE id_promocion = @idPromocion ORDER BY id_producto",
                new { idPromocion }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<PagedResult<Promocion>> SearchAsync(string? query, string? tipo, bool? esActivo, int pagina, int tamanoPagina, CancellationToken ct = default)
    {
        if (!TamanosPagina.EsValido(tamanoPagina))
        {
            throw new ArgumentException($"tamanoPagina inválido: {tamanoPagina}. Valores permitidos: {string.Join(", ", TamanosPagina.Validos)}.", nameof(tamanoPagina));
        }

        var paginaEfectiva = Math.Max(pagina, 1);
        var offset = (paginaEfectiva - 1) * tamanoPagina;
        var queryEfectivo = string.IsNullOrWhiteSpace(query) ? null : query.Trim();

        await using var conn = ConnectionFactory.Open(DbPath);

        var total = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM promociones " + SearchWhere,
                new { query = queryEfectivo, tipo, esActivo }, cancellationToken: ct));

        var rows = await conn.QueryAsync<Promocion>(
            new CommandDefinition(
                Select + SearchWhere +
                "ORDER BY es_activo DESC, tipo, nombre " +
                "LIMIT @tamanoPagina OFFSET @offset",
                new { query = queryEfectivo, tipo, esActivo, tamanoPagina, offset }, cancellationToken: ct));

        return new PagedResult<Promocion>
        {
            Items = rows.ToList(),
            TotalRegistros = total,
            Pagina = paginaEfectiva,
            TamanoPagina = tamanoPagina,
        };
    }

    public async Task UpdateAsync(Promocion promo, IReadOnlyList<PromocionComponente> componentes, CancellationToken ct = default)
    {
        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
            var affected = await conn.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE promociones SET nombre = @Nombre, descripcion = @Descripcion, id_producto = @IdProducto, id_plan = @IdPlan, " +
                    "tipo_descuento = @TipoDescuento, valor = @Valor, precio_combo_centavos = @PrecioComboCentavos, " +
                    "fecha_inicio = @FechaInicio, fecha_fin = @FechaFin, updated_at = @UpdatedAt " +
                    "WHERE id_promocion = @IdPromocion AND deleted_at IS NULL",
                    promo, tx, cancellationToken: ct));

            if (affected == 0)
            {
                throw BusinessException.NotFound("Promoción no encontrada", "promocion_no_encontrada");
            }

            await conn.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM promocion_productos WHERE id_promocion = @idPromocion",
                    new { idPromocion = promo.IdPromocion }, tx, cancellationToken: ct));

            await InsertarComponentesCoreAsync(conn, tx, promo.IdPromocion, componentes, ct);
        }, ct);
    }

    public async Task SetActivoAsync(string idPromocion, bool activo, string updatedAt, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var affected = await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE promociones SET es_activo = @activo, updated_at = @updatedAt " +
                "WHERE id_promocion = @idPromocion AND deleted_at IS NULL",
                new { idPromocion, activo, updatedAt }, cancellationToken: ct));

        if (affected == 0)
        {
            throw BusinessException.NotFound("Promoción no encontrada", "promocion_no_encontrada");
        }
    }

    public async Task<Promocion?> GetDescuentoSolapadoAsync(long idProducto, string? fechaInicio, string? fechaFin, string? excluirId, CancellationToken ct = default)
    {
        // Solape de rangos date-only: nuevo.inicio <= existente.fin (o fin null) AND
        // existente.inicio <= nuevo.fin (o inicio null). Ambos extremos abiertos siempre solapan.
        const string sql =
            Select +
            "WHERE deleted_at IS NULL AND es_activo = 1 AND tipo = 'descuento' " +
            "AND id_producto = @idProducto " +
            "AND (@excluirId IS NULL OR id_promocion != @excluirId) " +
            "AND (@fechaInicio IS NULL OR fecha_fin IS NULL OR @fechaInicio <= fecha_fin) " +
            "AND (fecha_inicio IS NULL OR @fechaFin IS NULL OR fecha_inicio <= @fechaFin) " +
            "ORDER BY updated_at DESC LIMIT 1";

        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Promocion>(
            new CommandDefinition(sql, new { idProducto, fechaInicio, fechaFin, excluirId }, cancellationToken: ct));
    }

    public async Task<Promocion?> GetDescuentoVigentePorProductoAsync(long idProducto, string hoy, CancellationToken ct = default)
    {
        // Vigencia efectiva en SQL: activo, no borrado y dentro del rango respecto a hoy.
        const string sql =
            Select +
            "WHERE deleted_at IS NULL AND es_activo = 1 AND tipo = 'descuento' " +
            "AND id_producto = @idProducto " +
            "AND (fecha_inicio IS NULL OR fecha_inicio <= @hoy) " +
            "AND (fecha_fin IS NULL OR fecha_fin >= @hoy) " +
            "ORDER BY updated_at DESC LIMIT 1";

        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Promocion>(
            new CommandDefinition(sql, new { idProducto, hoy }, cancellationToken: ct));
    }

    private static async Task InsertarComponentesCoreAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx,
        string idPromocion,
        IReadOnlyList<PromocionComponente> componentes,
        CancellationToken ct)
    {
        foreach (var c in componentes)
        {
            await conn.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO promocion_productos (id_promocion, id_producto, cantidad) " +
                    "VALUES (@idPromocion, @idProducto, @cantidad);",
                    new { idPromocion, idProducto = c.IdProducto, cantidad = c.Cantidad }, tx, cancellationToken: ct));
        }
    }
}
