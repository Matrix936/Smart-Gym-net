using Dapper;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class UsuariosRepository : RepositoryBase, IUsuariosRepository
{
    public UsuariosRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<long> InsertAsync(Usuario usuario, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.ExecuteScalarAsync<long>(
            new CommandDefinition(
                "INSERT INTO usuarios (nombre, apellido_paterno, apellido_materno, email, password_hash, " +
                "id_rol, id_sede, es_activo, created_at, updated_at) " +
                "VALUES (@Nombre, @ApellidoPaterno, @ApellidoMaterno, @Email, @PasswordHash, " +
                "@IdRol, @IdSede, @EsActivo, @CreatedAt, @UpdatedAt); " +
                "SELECT last_insert_rowid();",
                new
                {
                    usuario.Nombre,
                    usuario.ApellidoPaterno,
                    usuario.ApellidoMaterno,
                    usuario.Email,
                    usuario.PasswordHash,
                    usuario.IdRol,
                    usuario.IdSede,
                    usuario.EsActivo,
                    usuario.CreatedAt,
                    usuario.UpdatedAt,
                },
                cancellationToken: ct));
    }

    public async Task<Usuario?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Usuario>(
            new CommandDefinition(
                "SELECT id_usuario, nombre, apellido_paterno, apellido_materno, email, password_hash, " +
                "id_rol, id_sede, es_activo, updated_at, sincronizado, deleted_at, created_at " +
                "FROM usuarios WHERE email = @email COLLATE NOCASE AND deleted_at IS NULL",
                new { email }, cancellationToken: ct));
    }

    public async Task<Usuario?> GetByIdAsync(long idUsuario, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Usuario>(
            new CommandDefinition(
                "SELECT id_usuario, nombre, apellido_paterno, apellido_materno, email, password_hash, " +
                "id_rol, id_sede, es_activo, updated_at, sincronizado, deleted_at, created_at " +
                "FROM usuarios WHERE id_usuario = @id",
                new { id = idUsuario }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Usuario>> GetActivosAsync(CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<Usuario>(
            new CommandDefinition(
                "SELECT id_usuario, nombre, apellido_paterno, apellido_materno, email, password_hash, " +
                "id_rol, id_sede, es_activo, updated_at, sincronizado, deleted_at, created_at " +
                "FROM usuarios WHERE es_activo = 1 AND deleted_at IS NULL ORDER BY nombre, apellido_paterno",
                cancellationToken: ct));
        return rows.ToList();
    }

    public async Task UpdatePerfilAsync(long idUsuario, string nombre, string apellidoPaterno, string apellidoMaterno,
        string email, string updatedAt, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE usuarios SET nombre = @nombre, apellido_paterno = @apeP, apellido_materno = @apeM, " +
                "email = @email COLLATE NOCASE, updated_at = @updatedAt " +
                "WHERE id_usuario = @idUsuario AND deleted_at IS NULL",
                new { idUsuario, nombre, apeP = apellidoPaterno, apeM = apellidoMaterno, email, updatedAt },
                cancellationToken: ct));
    }

    public async Task UpdatePasswordAsync(long idUsuario, string passwordHash, string updatedAt, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE usuarios SET password_hash = @passwordHash, updated_at = @updatedAt " +
                "WHERE id_usuario = @idUsuario AND deleted_at IS NULL",
                new { idUsuario, passwordHash, updatedAt }, cancellationToken: ct));
    }
}