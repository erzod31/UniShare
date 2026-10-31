using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UniShare.Infrastructure;

/// <summary>Downloads only immutable public model files from the shipped catalog. Never accepts text.</summary>
public sealed class TranslationAssetCache : IDisposable
{
    private readonly string _cacheRoot;
    private readonly Dictionary<string, TranslationModelFile> _files;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TranslationAssetCache(string catalogJson, string cacheRoot, HttpMessageHandler? handler = null)
    {
        _cacheRoot = Path.GetFullPath(cacheRoot);
        var catalog = JsonSerializer.Deserialize<ModelCatalog>(catalogJson)
            ?? throw new InvalidDataException("Invalid translation catalog.");
        _files = [];
        foreach (var pair in catalog.Pairs.Values)
        {
            foreach (var file in pair.Files.Values)
            {
                if (!IsHash(file.Sha256) || !IsHash(file.DecompressedSha256) ||
                    file.Size is < 1 or > 67_108_864 || file.DecompressedSize is < 1 or > 134_217_728 ||
                    !Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                    uri.Host != "firefox-settings-attachments.cdn.mozilla.net" ||
                    !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
                    !string.IsNullOrEmpty(uri.Fragment) || !uri.IsDefaultPort)
                {
                    throw new InvalidDataException("Invalid translation model entry.");
                }
                if (_files.TryGetValue(file.Sha256, out var previous) &&
                    (previous.Size != file.Size || previous.DecompressedSha256 != file.DecompressedSha256))
                {
                    throw new InvalidDataException("Conflicting translation model entries.");
                }
                _files[file.Sha256] = file;
            }
        }
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromMinutes(3);
    }

    public async Task<string> GetAsync(string hash, CancellationToken cancellationToken = default)
    {
        if (!IsHash(hash) || !_files.TryGetValue(hash, out var model))
        {
            throw new InvalidDataException("Unknown translation model.");
        }
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var destination = Path.Combine(_cacheRoot, hash);
        string? partial = null;
        try
        {
            if (await IsValidAsync(destination, model, cancellationToken).ConfigureAwait(false))
            {
                return destination;
            }
            Directory.CreateDirectory(_cacheRoot);
            partial = Path.Combine(_cacheRoot, $"{hash}.{Guid.NewGuid():N}.partial");
            using var response = await _httpClient.GetAsync(new Uri(model.Url),
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != model.Size)
            {
                throw new InvalidDataException("Translation model length mismatch.");
            }
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65_536, FileOptions.Asynchronous))
            {
                var buffer = new byte[65_536];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    total += read;
                    if (total > model.Size)
                    {
                        throw new InvalidDataException("Translation model length mismatch.");
                    }
                    hasher.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                if (total != model.Size ||
                    !Convert.ToHexStringLower(hasher.GetHashAndReset()).Equals(hash, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Translation model integrity mismatch.");
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(partial, destination, overwrite: true);
            return destination;
        }
        finally
        {
            if (partial is not null && File.Exists(partial))
            {
                File.Delete(partial);
            }
            _gate.Release();
        }
    }

    private static async Task<bool> IsValidAsync(string path, TranslationModelFile model, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != model.Size)
        {
            return false;
        }
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)) == model.Sha256;
    }

    private static bool IsHash(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public void Dispose()
    {
        _httpClient.Dispose();
        _gate.Dispose();
    }

    private sealed record ModelCatalog([property: JsonPropertyName("pairs")] Dictionary<string, ModelPair> Pairs);
    private sealed record ModelPair([property: JsonPropertyName("files")] Dictionary<string, TranslationModelFile> Files);
    private sealed record TranslationModelFile(
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("decompressedSha256")] string DecompressedSha256,
        [property: JsonPropertyName("decompressedSize")] long DecompressedSize);
}
