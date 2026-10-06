using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace UniShare.Infrastructure;

internal static class PortablePackageContract
{
    internal const int FormatVersion = 1;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    internal static async Task<T> ReadJsonAsync<T>(
        ZipArchive archive,
        string path,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"Falta {path}.");
        return await ReadJsonAsync<T>(entry, cancellationToken);
    }

    internal static async Task<T> ReadJsonAsync<T>(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken) ??
            throw new InvalidDataException($"JSON vacío o inválido en {entry.FullName}.");
    }

    internal static async Task<List<T>> ReadNdjsonAsync<T>(
        ZipArchive archive,
        string path,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidDataException($"Falta {path}.");
        return await ReadNdjsonAsync<T>(entry, cancellationToken);
    }

    internal static async Task<List<T>> ReadNdjsonAsync<T>(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        var result = new List<T>();
        foreach (var line in await ReadLinesAsync(entry, cancellationToken))
        {
            result.Add(JsonSerializer.Deserialize<T>(line, JsonOptions) ??
                throw new InvalidDataException($"Registro inválido en {entry.FullName}."));
        }

        return result;
    }

    internal static async Task<List<string>> ReadLinesAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(
            stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: false);
        var lines = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        return lines;
    }
}

internal sealed record BackupManifest(
    int FormatVersion,
    DateTimeOffset CreatedAtUtc,
    int ItemCount,
    int AssetCount,
    int TagCount,
    int CollectionCount,
    int BlobCount,
    long TotalBlobBytes,
    Guid? SourceDeviceId);

internal sealed record ItemRecord(
    string Id,
    int Kind,
    string Title,
    string? OriginalUrl,
    string? CanonicalUrl,
    string? Source,
    string? Author,
    string? Description,
    string CreatedAt,
    string UpdatedAt,
    string? DeletedAt,
    bool Favorite);

internal sealed record AssetRecord(
    string Id,
    string ItemId,
    string Sha256,
    long ByteLength,
    string MimeType,
    string OriginalName,
    string RelativePath,
    string CreatedAt);

internal sealed record LabelRecord(string Id, string Name, string NormalizedName);

internal sealed record MembershipRecord(string MembershipId, string ItemId, string LabelId, string? RemovedAt);
