using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace SmartGym.Data.Db;

/// <summary>
/// Crea y abre conexiones SQLite. Regla del schema (01-modelo-datos.md):
/// PRAGMA foreign_keys = ON en CADA conexión, equivalente a
/// create_sqlite_connection en el proyecto Rust (db.rs).
/// </summary>
public static class ConnectionFactory
{
    static ConnectionFactory()
    {
        // Convención del schema: columnas snake_case (id_usuario) ↔ entidades
        // PascalCase (IdUsuario). Dapper iguala omitiendo guiones bajos.
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public static SqliteConnection Open(string dbPath)
    {
        var fullPath = Path.GetFullPath(dbPath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
        };

        var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();

        return connection;
    }
}
