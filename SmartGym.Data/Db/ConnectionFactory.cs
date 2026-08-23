using System.Data;
using System.Globalization;
using System.Text;
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

        // Búsqueda insensible a acentos: los LIKE del proyecto comparan
        // sin_acentos(columna) contra sin_acentos(@query), de modo que "gomez"
        // encuentre "Gómez" (SQLite COLLATE NOCASE solo ignora mayúsculas ASCII).
        // NOTA de rendimiento: aplicar la función sobre la columna impide el uso
        // de índices en esa comparación (no sargable). Aceptable para el volumen
        // de catálogos/socios; NO replicar el patrón en tablas de alto volumen
        // (bitacora_auditoria, ventas) sin una columna normalizada persistida.
        connection.CreateFunction(
            "sin_acentos",
            (string? valor) => QuitarAcentos(valor),
            isDeterministic: true);

        return connection;
    }

    /// <summary>
    /// Normalización Unicode NFD + descarte de marcas diacríticas: cubre todos
    /// los acentos/tildes/diéresis del español (y latinos) sin tablas de reemplazo.
    /// </summary>
    private static string? QuitarAcentos(string? valor)
    {
        if (string.IsNullOrEmpty(valor))
        {
            return valor;
        }

        var forma = valor.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(forma.Length);
        foreach (var c in forma)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
