using Dapper;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class CobrosRecordatoriosRepository : RepositoryBase, ICobrosRecordatoriosRepository
{
    public CobrosRecordatoriosRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task InsertConBitacoraAsync(
        CobroRecordatorio recordatorio,
        BitacoraAuditoria bitacora,
        CancellationToken ct = default)
    {
        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
            await conn.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO cobros_recordatorios (id_recordatorio, id_socio, tipo, " +
                    "fecha_envio, resultado, updated_at, sincronizado) " +
                    "VALUES (@IdRecordatorio, @IdSocio, @Tipo, @FechaEnvio, @Resultado, @UpdatedAt, 0);",
                    recordatorio, tx, cancellationToken: ct));

            await conn.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO bitacora_auditoria (id_registro, id_usuario, accion, tabla_afectada, " +
                    "id_registro_afectado, valor_anterior, valor_nuevo, id_sede, created_at, updated_at, sincronizado) " +
                    "VALUES (@IdRegistro, @IdUsuario, @Accion, @TablaAfectada, " +
                    "@IdRegistroAfectado, @ValorAnterior, @ValorNuevo, @IdSede, @CreatedAt, @UpdatedAt, 0);",
                    bitacora, tx, cancellationToken: ct));
        }, ct);
    }
}