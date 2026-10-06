using System.Globalization;
using Microsoft.Data.Sqlite;

namespace UniShare.Infrastructure;

public static class LibraryProfileValidator
{
    private const int CurrentSchemaVersion = 4;
    private static readonly string[] RequiredTables = ["schema_info", "settings", "items", "assets"];

    public static async Task ValidateExistingAsync(
        string profileRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileRoot);
        var root = Path.GetFullPath(profileRoot);
        var databasePath = Path.Combine(root, "library.db");
        if (!File.Exists(databasePath))
        {
            return;
        }

        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                ForeignKeys = true,
                DefaultTimeout = 5,
            }.ToString());
            await connection.OpenAsync(cancellationToken);

            await using (var integrity = connection.CreateCommand())
            {
                integrity.CommandText = "PRAGMA quick_check;";
                var result = Convert.ToString(
                    await integrity.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"La base de datos no supera la comprobación de integridad: {result}");
                }
            }

            foreach (var table in RequiredTables)
            {
                await using var find = connection.CreateCommand();
                find.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1;";
                find.Parameters.AddWithValue("$name", table);
                if (await find.ExecuteScalarAsync(cancellationToken) is null)
                {
                    throw new InvalidDataException("La carpeta contiene una base de datos que no pertenece a UniShare.");
                }
            }

            await using var version = connection.CreateCommand();
            version.CommandText = "SELECT version FROM schema_info LIMIT 1;";
            var schemaVersion = Convert.ToInt32(
                await version.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            if (schemaVersion is < 1 or > CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    $"La biblioteca usa una versión no compatible ({schemaVersion}).");
            }
        }
        catch (SqliteException exception)
        {
            throw new InvalidDataException(
                "No se pudo validar library.db. Elige una biblioteca UniShare válida o una carpeta sin library.db.",
                exception);
        }
    }
}
