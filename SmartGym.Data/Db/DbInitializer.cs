using System.Reflection;
using Microsoft.Data.Sqlite;

namespace SmartGym.Data.Db;

/// <summary>
/// Aplica el schema (scripts/schema_smart_gym.sql) en cada arranque. El script
/// es idempotente por diseño (CREATE IF NOT EXISTS + seed con INSERT OR IGNORE /
/// WHERE NOT EXISTS), así que ejecutarlo siempre es lo que permite que las
/// tablas nuevas del catálogo lleguen a bases de datos ya creadas sin un
/// mecanismo de migraciones. Costo: milisegundos de DDL IF NOT EXISTS.
/// </summary>
public static class DbInitializer
{
    private const string SchemaResource = "SmartGym.Data.Scripts.schema_smart_gym.sql";

    public static void Initialize(string dbPath)
    {
        using var connection = ConnectionFactory.Open(dbPath);

        var script = ReadSchemaScript();

        using var tx = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = script;
        command.ExecuteNonQuery();
        tx.Commit();
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
