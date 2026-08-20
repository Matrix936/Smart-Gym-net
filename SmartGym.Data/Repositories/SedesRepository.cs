using Dapper;
using Microsoft.Data.Sqlite;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class SedesRepository : RepositoryBase, ISedesRepository
{
    public SedesRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<Sede?> GetByIdAsync(long idSede, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Sede>(
            new CommandDefinition(
                "SELECT id_sede, nombre, direccion, telefono, horario_apertura, horario_cierre, " +
                "es_activa, updated_at, sincronizado, deleted_at " +
                "FROM sedes WHERE id_sede = @id AND deleted_at IS NULL",
                new { id = idSede }, cancellationToken: ct));
    }

    public async Task<Sede?> GetPrincipalAsync(CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<Sede>(
            new CommandDefinition(
                "SELECT id_sede, nombre, direccion, telefono, horario_apertura, horario_cierre, " +
                "es_activa, updated_at, sincronizado, deleted_at " +
                "FROM sedes WHERE es_activa = 1 AND deleted_at IS NULL " +
                "ORDER BY id_sede ASC LIMIT 1",
                cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Sede>> GetActivasAsync(CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<Sede>(
            new CommandDefinition(
                "SELECT id_sede, nombre, direccion, telefono, horario_apertura, horario_cierre, " +
                "es_activa, updated_at, sincronizado, deleted_at " +
                "FROM sedes WHERE es_activa = 1 AND deleted_at IS NULL ORDER BY nombre",
                cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<long> InsertAsync(Sede sede, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "INSERT INTO sedes (nombre, direccion, telefono, horario_apertura, horario_cierre, " +
            "es_activa, updated_at, sincronizado) " +
            "VALUES (@Nombre, @Direccion, @Telefono, @HorarioApertura, @HorarioCierre, " +
            "@EsActiva, @UpdatedAt, 0); SELECT last_insert_rowid();",
            new
            {
                sede.Nombre,
                sede.Direccion,
                sede.Telefono,
                sede.HorarioApertura,
                sede.HorarioCierre,
                EsActiva = sede.EsActiva ? 1 : 0,
                UpdatedAt = string.IsNullOrEmpty(sede.UpdatedAt)
                    ? DateHelper.NowIsoUtc()
                    : sede.UpdatedAt,
            }, cancellationToken: ct));
    }
}