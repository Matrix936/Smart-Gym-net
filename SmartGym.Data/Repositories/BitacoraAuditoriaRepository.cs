using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class BitacoraAuditoriaRepository : RepositoryBase, IBitacoraAuditoriaRepository
{
    public BitacoraAuditoriaRepository(string dbPath) : base(dbPath)
    {
    }

    /// <summary>Último 'venta.cancelada' de la venta con actor resuelto (JOIN usuarios).</summary>
    public async Task<(string FechaIsoUtc, string Usuario)?> ObtenerUltimaCancelacionAsync(
        string idVenta, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var fila = await conn.QuerySingleOrDefaultAsync<(string? CreatedAt, string? Usuario)>(
            new CommandDefinition(
                "SELECT b.created_at, TRIM(u.nombre || ' ' || u.apellido_paterno) AS Usuario " +
                "FROM bitacora_auditoria b LEFT JOIN usuarios u ON u.id_usuario = b.id_usuario " +
                "WHERE b.accion = 'venta.cancelada' AND b.id_registro_afectado = @idVenta " +
                "AND b.deleted_at IS NULL ORDER BY b.created_at DESC LIMIT 1",
                new { idVenta }, cancellationToken: ct));
        return string.IsNullOrEmpty(fila.CreatedAt)
            ? null
            : (fila.CreatedAt, fila.Usuario ?? "-");
    }

    public async Task InsertAsync(BitacoraAuditoria registro, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO bitacora_auditoria (id_registro, id_usuario, accion, tabla_afectada, " +
                "id_registro_afectado, valor_anterior, valor_nuevo, id_sede, created_at, updated_at, sincronizado) " +
                "VALUES (@IdRegistro, @IdUsuario, @Accion, @TablaAfectada, " +
                "@IdRegistroAfectado, @ValorAnterior, @ValorNuevo, @IdSede, @CreatedAt, @UpdatedAt, 0);",
                registro, cancellationToken: ct));
    }

    public async Task<bool> NoExisteAccionParaAsync(string tablaAfectada, string idRegistroAfectado, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                "SELECT NOT EXISTS(SELECT 1 FROM bitacora_auditoria WHERE tabla_afectada = @tablaAfectada " +
                "AND id_registro_afectado = @idRegistroAfectado)",
                new { tablaAfectada, idRegistroAfectado }, cancellationToken: ct));
    }

    private const string BuscarFrom =
        "FROM bitacora_auditoria b " +
        "LEFT JOIN usuarios u ON u.id_usuario = b.id_usuario " +
        "LEFT JOIN sedes s ON s.id_sede = b.id_sede " +
        // Acciones globales (catálogos hechos por admin sin sede) son visibles
        // desde cualquier sede: id_sede NULL no se filtra. Con @idSede NULL
        // (modo "todas las sedes" de los listados) se devuelven todas.
        "WHERE b.deleted_at IS NULL AND (@idSede IS NULL OR b.id_sede IS NULL OR b.id_sede = @idSede) ";

    private const string BuscarSelect =
        "SELECT b.created_at AS Fecha, " +
        "b.accion AS Accion, " +
        "TRIM(u.nombre || ' ' || u.apellido_paterno) AS NombreUsuario, " +
        "b.tabla_afectada AS TablaAfectada, " +
        "b.id_registro_afectado AS IdRegistroAfectado, " +
        "b.valor_anterior AS ValorAnterior, " +
        "b.valor_nuevo AS ValorNuevo, " +
        "b.id_sede AS IdSede, " +
        "s.nombre AS SedeNombre ";

    public async Task<PagedResult<BitacoraHistorialDto>> BuscarAsync(
        long? idSede,
        BitacoraFiltros? filtros = null,
        int pagina = 1,
        int tamanoPagina = TamanosPagina.Default,
        CancellationToken ct = default)
    {
        if (!TamanosPagina.EsValido(tamanoPagina))
        {
            throw new ArgumentException($"tamanoPagina inválido: {tamanoPagina}. Valores permitidos: {string.Join(", ", TamanosPagina.Validos)}.", nameof(tamanoPagina));
        }

        var paginaEfectiva = Math.Max(pagina, 1);
        var offset = (paginaEfectiva - 1) * tamanoPagina;

        filtros ??= new BitacoraFiltros();

        var where = BuscarFrom;
        var parametros = new DynamicParameters();
        parametros.Add("idSede", idSede);
        parametros.Add("tamanoPagina", tamanoPagina);
        parametros.Add("offset", offset);

        if (!string.IsNullOrEmpty(filtros.Desde))
        {
            where += "AND b.created_at >= @desde ";
            parametros.Add("desde", filtros.Desde);
        }
        if (!string.IsNullOrEmpty(filtros.Hasta))
        {
            where += "AND b.created_at <= @hasta ";
            parametros.Add("hasta", filtros.Hasta);
        }
        if (!string.IsNullOrEmpty(filtros.Categoria))
        {
            where += "AND b.accion LIKE @categoria || '%' ";
            parametros.Add("categoria", filtros.Categoria.TrimEnd('.'));
        }
        if (!string.IsNullOrEmpty(filtros.Accion))
        {
            where += "AND b.accion = @accion ";
            parametros.Add("accion", filtros.Accion);
        }
        if (filtros.IdUsuario is not null)
        {
            where += "AND b.id_usuario = @idUsuario ";
            parametros.Add("idUsuario", filtros.IdUsuario);
        }

        await using var conn = ConnectionFactory.Open(DbPath);

        var total = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) " + where,
                parametros, cancellationToken: ct));

        var rows = await conn.QueryAsync<BitacoraHistorialDto>(
            new CommandDefinition(
                BuscarSelect + where +
                "ORDER BY b.created_at DESC " +
                "LIMIT @tamanoPagina OFFSET @offset",
                parametros, cancellationToken: ct));

        return new PagedResult<BitacoraHistorialDto>
        {
            Items = rows.ToList(),
            TotalRegistros = total,
            Pagina = paginaEfectiva,
            TamanoPagina = tamanoPagina,
        };
    }
}