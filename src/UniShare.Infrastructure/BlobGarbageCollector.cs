using Microsoft.Data.Sqlite;

namespace UniShare.Infrastructure;

public sealed record BlobGarbageCollectionResult(int DeletedBlobs, long ReclaimedBytes);

/// <summary>
/// Conservatively removes canonical CAS files that have not been referenced for a grace period.
/// Run this before accepting writes so the reference snapshot cannot race with a new attachment.
/// </summary>
public sealed class BlobGarbageCollector(string profileRoot, TimeProvider timeProvider)
{
    private readonly string _profileRoot = Path.GetFullPath(profileRoot);
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async Task<BlobGarbageCollectionResult> CollectAsync(
        TimeSpan gracePeriod,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gracePeriod, TimeSpan.Zero);
        var databasePath = Path.Combine(_profileRoot, "library.db");
        var blobRoot = Path.Combine(_profileRoot, "blobs");
        if (!File.Exists(databasePath) || !Directory.Exists(blobRoot))
        {
            return new BlobGarbageCollectionResult(0, 0);
        }

        var referenced = await ReadReferencedHashesAsync(databasePath, cancellationToken);
        var cutoff = _timeProvider.GetUtcNow() - gracePeriod;
        var deleted = 0;
        long reclaimed = 0;
        foreach (var path in Directory.EnumerateFiles(blobRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(path);
            var hash = info.Name;
            if (!IsCanonicalHash(hash) || referenced.Contains(hash) ||
                info.LastWriteTimeUtc > cutoff.UtcDateTime ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var expected = Path.Combine(blobRoot, hash[..2], hash.Substring(2, 2), hash);
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var length = info.Length;
            File.Delete(path);
            deleted++;
            reclaimed += length;
        }

        RemoveEmptyDirectories(blobRoot);
        return new BlobGarbageCollectionResult(deleted, reclaimed);
    }

    private static async Task<HashSet<string>> ReadReferencedHashesAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT lower(sha256) FROM assets;";
        var result = new HashSet<string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    private static bool IsCanonicalHash(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void RemoveEmptyDirectories(string blobRoot)
    {
        foreach (var directory in Directory.EnumerateDirectories(blobRoot, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }
}
