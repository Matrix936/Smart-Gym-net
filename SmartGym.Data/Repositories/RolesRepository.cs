using Dapper;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class RolesRepository : RepositoryBase, IRolesRepository
{
    public RolesRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<Rol?> GetByNameAsync(string nombre, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Rol>(
            new CommandDefinition(
                "SELECT id_rol, nombre, descripcion, created_at FROM roles WHERE nombre = @nombre",
                new { nombre }, cancellationToken: ct));
    }

    public async Task<Rol?> GetByIdAsync(long idRol, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Rol>(
            new CommandDefinition(
                "SELECT id_rol, nombre, descripcion, created_at FROM roles WHERE id_rol = @id",
                new { id = idRol }, cancellationToken: ct));
    }

    public async Task<long> InsertAsync(Rol rol, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.ExecuteScalarAsync<long>(
            new CommandDefinition(
                "INSERT INTO roles (nombre, descripcion, created_at) VALUES (@Nombre, @Descripcion, @CreatedAt); " +
                "SELECT last_insert_rowid();",
                new { rol.Nombre, rol.Descripcion, rol.CreatedAt }, cancellationToken: ct));
    }
}