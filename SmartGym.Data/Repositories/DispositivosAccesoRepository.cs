using Dapper;
using SmartGym.Core.Common;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class DispositivosAccesoRepository : RepositoryBase, IDispositivosAccesoRepository
{
    public DispositivosAccesoRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<bool> ExisteActivoEnSedeAsync(long idDispositivo, long idSede, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var count = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM dispositivos_acceso " +
            "WHERE id_dispositivo = @idDispositivo AND id_sede = @idSede " +
            "AND es_activo = 1 AND deleted_at IS NULL",
            new { idDispositivo, idSede }, cancellationToken: ct));
        return count > 0;
    }

    public async Task<long> InsertAsync(DispositivoAcceso dispositivo, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "INSERT INTO dispositivos_acceso (nombre, tipo, id_sede, es_activo, updated_at, sincronizado) " +
            "VALUES (@Nombre, @Tipo, @IdSede, @EsActivo, @UpdatedAt, 0); " +
            "SELECT last_insert_rowid();",
            new
            {
                dispositivo.Nombre,
                dispositivo.Tipo,
                dispositivo.IdSede,
                EsActivo = dispositivo.EsActivo ? 1 : 0,
                UpdatedAt = string.IsNullOrEmpty(dispositivo.UpdatedAt)
                    ? DateHelper.NowIsoUtc()
                    : dispositivo.UpdatedAt,
            }, cancellationToken: ct));
        return id;
    }
}