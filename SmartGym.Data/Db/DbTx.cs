using Microsoft.Data.Sqlite;

namespace SmartGym.Data.Db;

/// <summary>
/// Wrapper de transacciones (helper de Fase 1). Commit si la operación
/// termina sin excepción; rollback en caso contrario. La regla del schema
/// (foreign_keys = ON) la garantiza ConnectionFactory.
/// </summary>
public static class DbTx
{
    /// <summary>Ejecuta work dentro de una transacción sobre una conexión nueva.</summary>
    /// <param name="dbPath">Ruta de la base de datos.</param>
    /// <param name="work">Cuerpo de la transacción. Recibe (conexión, transacción).</param>
    public static async Task ExecuteAsync(
        string dbPath,
        Func<SqliteConnection, SqliteTransaction, Task> work,
        CancellationToken ct = default)
    {
        await using var connection = ConnectionFactory.Open(dbPath);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            await work(connection, tx);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}