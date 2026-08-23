using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class MaquinariaRepository : RepositoryBase, IMaquinariaRepository
{
    private const string Select = "SELECT id_maquina, nombre, descripcion, estado, id_sede, notas, " +
        "es_activo, updated_at, sincronizado, deleted_at, created_at FROM maquinaria ";

    public MaquinariaRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<Maquina?> GetByIdAsync(string idMaquina, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Maquina>(
            new CommandDefinition(
                Select + "WHERE id_maquina = @idMaquina AND es_activo = 1 AND deleted_at IS NULL",
                new { idMaquina }, cancellationToken: ct));
    }

    public async Task<Maquina?> GetByIdCualquierEstadoAsync(string idMaquina, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Maquina>(
            new CommandDefinition(
                Select + "WHERE id_maquina = @idMaquina AND deleted_at IS NULL",
                new { idMaquina }, cancellationToken: ct));
    }

    public async Task InsertAsync(Maquina maquina, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO maquinaria (id_maquina, nombre, descripcion, estado, id_sede, notas, " +
                "es_activo, updated_at, sincronizado, created_at) " +
                "VALUES (@IdMaquina, @Nombre, @Descripcion, @Estado, @IdSede, @Notas, " +
                "@EsActivo, @UpdatedAt, 0, @CreatedAt);",
                maquina, cancellationToken: ct));
    }

    public async Task UpdateAsync(Maquina maquina, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE maquinaria SET nombre = @Nombre, descripcion = @Descripcion, estado = @Estado, " +
                "notas = @Notas, updated_at = @UpdatedAt " +
                "WHERE id_maquina = @IdMaquina AND deleted_at IS NULL",
                maquina, cancellationToken: ct));
    }

    private const string SearchWhere =
        "WHERE deleted_at IS NULL AND id_sede = @idSede " +
        "AND (@nombre IS NULL OR sin_acentos(nombre) LIKE '%' || sin_acentos(@nombre) || '%' COLLATE NOCASE) " +
        "AND (@estado IS NULL OR estado = @estado) " +
        "AND (@esActivo IS NULL OR es_activo = @esActivo) ";

    public async Task<PagedResult<Maquina>> SearchAsync(
        long idSede,
        string? nombre,
        string? estado,
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
        var nombreTrim = string.IsNullOrWhiteSpace(nombre) ? null : nombre.Trim();
        var estadoTrim = string.IsNullOrWhiteSpace(estado) ? null : estado.Trim();

        await using var conn = ConnectionFactory.Open(DbPath);

        var total = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM maquinaria " + SearchWhere,
                new { idSede, nombre = nombreTrim, estado = estadoTrim, esActivo }, cancellationToken: ct));

        var rows = await conn.QueryAsync<Maquina>(
            new CommandDefinition(
                Select + SearchWhere +
                "ORDER BY nombre " +
                "LIMIT @tamanoPagina OFFSET @offset",
                new { idSede, nombre = nombreTrim, estado = estadoTrim, esActivo, tamanoPagina, offset },
                cancellationToken: ct));

        return new PagedResult<Maquina>
        {
            Items = rows.ToList(),
            TotalRegistros = total,
            Pagina = paginaEfectiva,
            TamanoPagina = tamanoPagina,
        };
    }

    public async Task DesactivarAsync(string idMaquina, string updatedAt, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE maquinaria SET es_activo = 0, updated_at = @updatedAt " +
                "WHERE id_maquina = @idMaquina AND deleted_at IS NULL",
                new { idMaquina, updatedAt }, cancellationToken: ct));
    }

    public async Task ActivarAsync(string idMaquina, string updatedAt, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE maquinaria SET es_activo = 1, updated_at = @updatedAt " +
                "WHERE id_maquina = @idMaquina AND deleted_at IS NULL",
                new { idMaquina, updatedAt }, cancellationToken: ct));
    }
}
