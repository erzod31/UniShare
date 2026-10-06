using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using UniShare.Infrastructure;

namespace UniShare.Benchmarks;

internal static class Program
{
    private const int ItemCount = 100_000;
    private const int MeasurementCount = 30;
    private const double MaximumP95Milliseconds = 300;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "unishare-benchmark", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "library.db");

        try
        {
            var repository = new SqliteLibraryRepository(databasePath);
            await repository.InitializeAsync();
            var generation = Stopwatch.StartNew();
            await GenerateItemsAsync(databasePath);
            generation.Stop();

            for (var index = 0; index < 5; index++)
            {
                _ = await repository.SearchAsync("especial 99990");
            }

            var samples = new List<double>(MeasurementCount);
            for (var index = 0; index < MeasurementCount; index++)
            {
                var stopwatch = Stopwatch.StartNew();
                var results = await repository.SearchAsync("especial 99990");
                stopwatch.Stop();
                if (results.Count != 1)
                {
                    throw new InvalidOperationException($"La búsqueda devolvió {results.Count} resultados en lugar de 1.");
                }

                samples.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            samples.Sort();
            var p95 = samples[(int)Math.Ceiling(samples.Count * 0.95) - 1];
            var report = new
            {
                ItemCount,
                Query = "especial 99990",
                Measurements = MeasurementCount,
                GenerationSeconds = generation.Elapsed.TotalSeconds,
                MinimumMilliseconds = samples[0],
                MedianMilliseconds = samples[samples.Count / 2],
                P95Milliseconds = p95,
                MaximumAllowedP95Milliseconds = MaximumP95Milliseconds,
                Passed = p95 <= MaximumP95Milliseconds,
            };
            Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
            return report.Passed ? 0 : 2;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task GenerateItemsAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Foreign Keys=True");
        await connection.OpenAsync();
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO items(
                id, kind, title, original_url, canonical_url, source, author, description,
                created_at, updated_at, deleted_at, favorite)
            VALUES($id, 1, $title, $url, $url, 'benchmark', 'UniShare', $description,
                $created_at, $created_at, NULL, 0);
            """;
        var id = command.Parameters.Add("$id", SqliteType.Text);
        var title = command.Parameters.Add("$title", SqliteType.Text);
        var url = command.Parameters.Add("$url", SqliteType.Text);
        var description = command.Parameters.Add("$description", SqliteType.Text);
        var createdAt = command.Parameters.Add("$created_at", SqliteType.Text);
        createdAt.Value = new DateTimeOffset(2026, 9, 24, 12, 0, 0, TimeSpan.Zero)
            .ToString("O", CultureInfo.InvariantCulture);

        for (var index = 0; index < ItemCount; index++)
        {
            id.Value = Guid.NewGuid().ToString("D");
            title.Value = index % 10 == 0 ? $"Documento especial {index}" : $"Documento ordinario {index}";
            url.Value = $"https://example.com/items/{index}";
            description.Value = $"Texto indexado para la entrada número {index}.";
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
        await using var optimize = connection.CreateCommand();
        optimize.CommandText = "PRAGMA optimize;";
        await optimize.ExecuteNonQueryAsync();
    }
}
