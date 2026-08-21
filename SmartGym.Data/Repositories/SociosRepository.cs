using Dapper;
using Microsoft.Data.Sqlite;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Errors;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class SociosRepository : RepositoryBase, ISociosRepository
{
    private const string Select = "SELECT id_socio, nombre, apellido_paterno, apellido_materno, email, telefono, " +
        "fecha_nacimiento, id_sede_registro, foto_path, contacto_emergencia_nombre, contacto_emergencia_telefono, " +
        "estado, fecha_ultimo_acceso, updated_at, sincronizado, deleted_at, created_at FROM socios ";

    private const string SearchWhere =
        "WHERE deleted_at IS NULL " +
        "AND (@query IS NULL OR nombre LIKE '%' || @query || '%' COLLATE NOCASE " +
        "OR email LIKE '%' || @query || '%' COLLATE NOCASE " +
        "OR telefono LIKE '%' || @query || '%' COLLATE NOCASE) " +
        "AND (@estado IS NULL OR estado = @estado COLLATE NOCASE) ";

    public SociosRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task InsertAsync(Socio socio, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await InsertCoreAsync(conn, null, socio, ct);
    }

    public async Task CrearConBitacoraAsync(Socio socio, BitacoraAuditoria bitacora, CancellationToken ct = default)
    {
        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
            await InsertCoreAsync(conn, tx, socio, ct);
            await InsertBitacoraCoreAsync(conn, tx, bitacora, ct);
        }, ct);
    }

    public async Task<Socio?> GetByIdAsync(string idSocio, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Socio>(
            new CommandDefinition(
                Select + "WHERE id_socio = @id AND deleted_at IS NULL",
                new { id = idSocio }, cancellationToken: ct));
    }

    public async Task<bool> ExistsAsync(string idSocio, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                "SELECT EXISTS(SELECT 1 FROM socios WHERE id_socio = @id AND deleted_at IS NULL)",
                new { id = idSocio }, cancellationToken: ct));
    }

    public async Task<PagedResult<Socio>> SearchAsync(string? query = null, string? estado = null, int pagina = 1, int tamanoPagina = TamanosPagina.Default, CancellationToken ct = default)
    {
        if (!TamanosPagina.EsValido(tamanoPagina))
        {
            throw new ArgumentException($"tamanoPagina inválido: {tamanoPagina}. Valores permitidos: {string.Join(", ", TamanosPagina.Validos)}.", nameof(tamanoPagina));
        }

        var paginaEfectiva = Math.Max(pagina, 1);
        var offset = (paginaEfectiva - 1) * tamanoPagina;

        await using var conn = ConnectionFactory.Open(DbPath);

        var total = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM socios " + SearchWhere,
                new { query, estado }, cancellationToken: ct));

        var rows = await conn.QueryAsync<Socio>(
            new CommandDefinition(
                Select + SearchWhere +
                "ORDER BY nombre, apellido_paterno " +
                "LIMIT @tamanoPagina OFFSET @offset",
                new { query, estado, tamanoPagina, offset }, cancellationToken: ct));

        return new PagedResult<Socio>
        {
            Items = rows.ToList(),
            TotalRegistros = total,
            Pagina = paginaEfectiva,
            TamanoPagina = tamanoPagina,
        };
    }

    public async Task UpdateAsync(Socio socio, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE socios SET nombre = @Nombre, apellido_paterno = @ApellidoPaterno, " +
                "apellido_materno = @ApellidoMaterno, email = @Email, telefono = @Telefono, " +
                "fecha_nacimiento = @FechaNacimiento, foto_path = @FotoPath, " +
                "contacto_emergencia_nombre = @ContactoEmergenciaNombre, " +
                "contacto_emergencia_telefono = @ContactoEmergenciaTelefono " +
                "WHERE id_socio = @IdSocio AND deleted_at IS NULL",
                new
                {
                    socio.IdSocio,
                    socio.Nombre,
                    socio.ApellidoPaterno,
                    socio.ApellidoMaterno,
                    socio.Email,
                    socio.Telefono,
                    socio.FechaNacimiento,
                    socio.FotoPath,
                    socio.ContactoEmergenciaNombre,
                    socio.ContactoEmergenciaTelefono,
                },
                cancellationToken: ct));
    }

    public async Task ActualizarConBitacoraAsync(Socio socio, BitacoraAuditoria bitacora, CancellationToken ct = default)
    {
        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
            var affected = await conn.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE socios SET nombre = @Nombre, apellido_paterno = @ApellidoPaterno, " +
                    "apellido_materno = @ApellidoMaterno, email = @Email, telefono = @Telefono, " +
                    "fecha_nacimiento = @FechaNacimiento, foto_path = @FotoPath, " +
                    "contacto_emergencia_nombre = @ContactoEmergenciaNombre, " +
                    "contacto_emergencia_telefono = @ContactoEmergenciaTelefono " +
                    "WHERE id_socio = @IdSocio AND deleted_at IS NULL",
                    new
                    {
                        socio.IdSocio,
                        socio.Nombre,
                        socio.ApellidoPaterno,
                        socio.ApellidoMaterno,
                        socio.Email,
                        socio.Telefono,
                        socio.FechaNacimiento,
                        socio.FotoPath,
                        socio.ContactoEmergenciaNombre,
                        socio.ContactoEmergenciaTelefono,
                    },
                    tx, cancellationToken: ct));

            if (affected == 0)
            {
                throw BusinessException.NotFound("Socio no encontrado", "socio_no_encontrado");
            }

            await InsertBitacoraCoreAsync(conn, tx, bitacora, ct);
        }, ct);
    }

    public async Task SoftDeleteAsync(string idSocio, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE socios SET deleted_at = @deletedAt WHERE id_socio = @id AND deleted_at IS NULL",
                new { id = idSocio, deletedAt = Core.Common.DateHelper.NowIsoUtc() }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<SocioHistorialEstado>> HistorialDeAsync(string idSocio, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<SocioHistorialEstado>(
            new CommandDefinition(
                "SELECT id, id_socio, estado_anterior, estado_nuevo, motivo, id_usuario, " +
                "created_at, updated_at, sincronizado, deleted_at " +
                "FROM socios_historial_estado WHERE id_socio = @idSocio AND deleted_at IS NULL ORDER BY created_at",
                new { idSocio }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task CambiarEstadoConBitacoraAsync(
        string idSocio,
        string estadoNuevo,
        SocioHistorialEstado historial,
        BitacoraAuditoria bitacora,
        CancellationToken ct = default)
    {
        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
            var affected = await conn.ExecuteAsync(
                new CommandDefinition(
                    "UPDATE socios SET estado = @estadoNuevo WHERE id_socio = @idSocio AND deleted_at IS NULL",
                    new { idSocio, estadoNuevo }, tx, cancellationToken: ct));

            if (affected == 0)
            {
                // Guardia de carrera: la existencia ya se validó en el servicio.
                throw BusinessException.NotFound("Socio no encontrado", "socio_no_encontrado");
            }

            await conn.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO socios_historial_estado (id, id_socio, estado_anterior, estado_nuevo, motivo, " +
                    "id_usuario, created_at, updated_at, sincronizado) " +
                    "VALUES (@Id, @IdSocio, @EstadoAnterior, @EstadoNuevo, @Motivo, " +
                    "@IdUsuario, @CreatedAt, @UpdatedAt, @Sincronizado);",
                    historial, tx, cancellationToken: ct));

            await InsertBitacoraCoreAsync(conn, tx, bitacora, ct);
        }, ct);
    }

    private static async Task InsertCoreAsync(SqliteConnection conn, SqliteTransaction? tx, Socio socio, CancellationToken ct)
    {
        await conn.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO socios (id_socio, nombre, apellido_paterno, apellido_materno, email, telefono, " +
                "fecha_nacimiento, id_sede_registro, foto_path, contacto_emergencia_nombre, " +
                "contacto_emergencia_telefono, estado, created_at, updated_at, sincronizado) " +
                "VALUES (@IdSocio, @Nombre, @ApellidoPaterno, @ApellidoMaterno, @Email, @Telefono, " +
                "@FechaNacimiento, @IdSedeRegistro, @FotoPath, @ContactoEmergenciaNombre, " +
                "@ContactoEmergenciaTelefono, @Estado, @CreatedAt, @UpdatedAt, 0);",
                new
                {
                    socio.IdSocio,
                    socio.Nombre,
                    socio.ApellidoPaterno,
                    socio.ApellidoMaterno,
                    socio.Email,
                    socio.Telefono,
                    socio.FechaNacimiento,
                    socio.IdSedeRegistro,
                    socio.FotoPath,
                    socio.ContactoEmergenciaNombre,
                    socio.ContactoEmergenciaTelefono,
                    socio.Estado,
                    socio.CreatedAt,
                    socio.UpdatedAt,
                },
                tx, cancellationToken: ct));
    }

    private static async Task InsertBitacoraCoreAsync(SqliteConnection conn, SqliteTransaction? tx, BitacoraAuditoria b, CancellationToken ct)
    {
        await conn.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO bitacora_auditoria (id_registro, id_usuario, accion, tabla_afectada, " +
                "id_registro_afectado, valor_anterior, valor_nuevo, id_sede, created_at, updated_at, sincronizado) " +
                "VALUES (@IdRegistro, @IdUsuario, @Accion, @TablaAfectada, " +
                "@IdRegistroAfectado, @ValorAnterior, @ValorNuevo, @IdSede, @CreatedAt, @UpdatedAt, 0);",
                b, tx, cancellationToken: ct));
    }
}