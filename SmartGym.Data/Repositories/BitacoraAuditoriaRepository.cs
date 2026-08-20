using Dapper;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class BitacoraAuditoriaRepository : RepositoryBase, IBitacoraAuditoriaRepository
{
    public BitacoraAuditoriaRepository(string dbPath) : base(dbPath)
    {
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
}