using System.Reflection;
using Microsoft.Data.Sqlite;

namespace SmartGym.Data.Db;

/// <summary>
/// Aplica el schema (scripts/schema_smart_gym.sql) la primera vez que se abre
/// la base de datos. El script es idempotente (CREATE IF NOT EXISTS + seed
/// con INSERT OR IGNORE / WHERE NOT EXISTS), pero solo se ejecuta cuando la
/// BD está vacía para no penalizar los arranques posteriores.
/// </summary>
public static class DbInitializer
{
    private const string SchemaResource = "SmartGym.Data.Scripts.schema_smart_gym.sql";

    public static void Initialize(string dbPath)
    {
        using var connection = ConnectionFactory.Open(dbPath);

        if (IsInitialized(connection))
        {
            return;
        }

        var script = ReadSchemaScript();

        using var tx = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = script;
        command.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>Detecta si ya existen objetos del schema (tablas/triggers, sin contar los sqlite_*).</summary>
    private static bool IsInitialized(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type IN ('table','trigger') " +
            "AND name NOT LIKE 'sqlite_%' " +
            "AND name != 'schema_migrations';";
        var count = (long)(command.ExecuteScalar() ?? 0L);
        return count > 0;
    }

    public static string ReadSchemaScript()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(SchemaResource)
            ?? throw new InvalidOperationException($"Recurso embebido no encontrado: {SchemaResource}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
