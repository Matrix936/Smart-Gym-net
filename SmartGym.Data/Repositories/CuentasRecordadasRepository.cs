using Dapper;
using SmartGym.Core.Entities;
using SmartGym.Core.Repositories;
using SmartGym.Data.Db;

namespace SmartGym.Data.Repositories;

public sealed class CuentasRecordadasRepository : RepositoryBase, ICuentasRecordadasRepository
{
    public CuentasRecordadasRepository(string dbPath) : base(dbPath)
    {
    }

    public async Task<IReadOnlyList<CuentaRecordadaLocal>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        var rows = await conn.QueryAsync<CuentaRecordadaLocal>(
            new CommandDefinition(
                "SELECT id_usuario, nombre, email, ultimo_login FROM cuentas_recordadas_local ORDER BY ultimo_login DESC",
                cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<CuentaRecordadaLocal?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        await using var conn = ConnectionFactory.Open(DbPath);
        return await conn.QuerySingleOrDefaultAsync<CuentaRecordadaLocal>(
            new CommandDefinition(
                "SELECT id_usuario, nombre, email, ultimo_login FROM cuentas_recordadas_local " +
                "WHERE email = @email COLLATE NOCASE",
                new { email }, cancellationToken: ct));
    }

    /// <summary>
    /// Upsert: borra coincidencias previas por id_usuario O email, luego inserta.
    /// La tabla tiene PK id_usuario y UNIQUE email → el borrado previo evita
    /// duplicados (segundo_login_actualiza_ultimo_login_sin_duplicar).
    /// </summary>
    public async Task UpsertAsync(CuentaRecordadaLocal cuenta, CancellationToken ct = default)
    {
        await DbTx.ExecuteAsync(DbPath, async (conn, tx) =>
        {
            await conn.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM cuentas_recordadas_local WHERE id_usuario = @IdUsuario OR email = @Email COLLATE NOCASE",
                    new { cuenta.IdUsuario, cuenta.Email }, tx, cancellationToken: ct));

            await conn.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO cuentas_recordadas_local (id_usuario, nombre, email, ultimo_login) " +
                    "VALUES (@IdUsuario, @Nombre, @Email, @UltimoLogin);",
                    cuenta, tx, cancellationToken: ct));
        }, ct);
    }
}