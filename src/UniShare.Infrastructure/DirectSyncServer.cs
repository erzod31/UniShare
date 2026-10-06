using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UniShare.Application;

namespace UniShare.Infrastructure;

public sealed record DirectSyncConfiguration(int Port, string PairingKey, string? PublicEndpoint = null)
{
    public string LocalAddress => $"http://127.0.0.1:{Port}";
    public string TailscaleServeCommand => $"tailscale serve --bg --https=8443 {Port}";
}

public sealed class DirectSyncServer : IAsyncDisposable
{
    private const long MaximumPackageBytes = 4L * 1024 * 1024 * 1024;
    private const long MaximumInventoryBytes = 8L * 1024 * 1024;
    private const long MaximumBrowserCaptureBytes = 12L * 1024 * 1024;
    private const int MaximumInventoryHashes = 100_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _profileRoot;
    private readonly string _databasePath;
    private readonly IBackupService _backup;
    private readonly ISyncService _merge;
    private readonly LibraryService? _library;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private WebApplication? _application;

    public DirectSyncServer(
        string profileRoot,
        IBackupService backup,
        ISyncService merge,
        int? port = null,
        LibraryService? library = null)
    {
        _profileRoot = Path.GetFullPath(profileRoot);
        _databasePath = Path.Combine(_profileRoot, "library.db");
        Configuration = LoadOrCreateConfiguration(port);
        _backup = backup ?? throw new ArgumentNullException(nameof(backup));
        _merge = merge ?? throw new ArgumentNullException(nameof(merge));
        _library = library;
    }

    public DirectSyncConfiguration Configuration { get; private set; }

    public bool IsRunning => _application is not null;

    public void UpdatePublicEndpoint(string rawEndpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawEndpoint);
        var endpoint = rawEndpoint.Trim().TrimEnd('/');
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !uri.Host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/")
        {
            throw new ArgumentException(
                "Usa la dirección HTTPS de Tailscale Serve terminada en .ts.net, sin rutas adicionales.",
                nameof(rawEndpoint));
        }

        var updated = Configuration with { PublicEndpoint = endpoint };
        SaveConfiguration(updated);
        Configuration = updated;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_application is not null)
        {
            return;
        }

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(Configuration.LocalAddress);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaximumPackageBytes);
        var app = builder.Build();

        app.MapGet("/api/v1/status", async context =>
        {
            if (!Authenticate(context))
            {
                return;
            }

            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            var syncState = await ReadSyncCursorStateAsync(null, context.RequestAborted);
            await context.Response.WriteAsJsonAsync(new
            {
                protocol_version = 2,
                revision = ReadRevision(),
                device_id = syncState.DeviceId,
                cursor = syncState.Through,
                service = "UniShare direct sync",
            }, cancellationToken: context.RequestAborted);
        });

        app.MapPost("/api/v1/sync", HandleSyncAsync);
        app.MapPost("/api/v1/blobs/missing", HandleMissingBlobsAsync);
        app.MapPost("/api/v1/capture", HandleCaptureAsync);
        await app.StartAsync(cancellationToken);
        _application = app;
    }

    private async Task HandleCaptureAsync(HttpContext context)
    {
        if (!Authenticate(context))
        {
            return;
        }
        if (_library is null)
        {
            await WriteProblemAsync(context, StatusCodes.Status404NotFound, "La captura del navegador no está habilitada.");
            return;
        }
        var bodySize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false })
        {
            bodySize.MaxRequestBodySize = MaximumBrowserCaptureBytes;
        }
        if (context.Request.ContentLength is > MaximumBrowserCaptureBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        string? renderedPath = null;
        try
        {
            var request = await context.Request.ReadFromJsonAsync<BrowserCaptureRequest>(
                JsonOptions, context.RequestAborted) ??
                throw new InvalidDataException("La solicitud de captura está vacía.");
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                throw new InvalidDataException("La captura necesita un enlace HTTP o HTTPS válido.");
            }
            var title = string.IsNullOrWhiteSpace(request.Title)
                ? LinkCaptureDefaults.TitleFromUrl(uri)
                : request.Title.Trim();
            var collections = string.IsNullOrWhiteSpace(request.Collection)
                ? Array.Empty<string>()
                : new[] { request.Collection.Trim() };
            if (request.SaveOffline)
            {
                if (string.IsNullOrWhiteSpace(request.RenderedText))
                {
                    throw new InvalidDataException(
                        "La copia offline necesita el texto renderizado de la pestaña.");
                }

                var stagingRoot = Path.Combine(_profileRoot, "staging");
                Directory.CreateDirectory(stagingRoot);
                renderedPath = Path.Combine(stagingRoot, $"browser-{Guid.NewGuid():N}.offline.html");
                await SafeHtmlSnapshot.WriteRenderedAsync(
                    renderedPath,
                    uri,
                    title,
                    request.RenderedText,
                    request.Images ?? [],
                    context.RequestAborted);
            }

            var item = await _library.CreateAsync(new CreateItemRequest(
                title,
                uri.AbsoluteUri,
                renderedPath,
                Source: string.IsNullOrWhiteSpace(request.SiteName)
                    ? "Extensión Chromium"
                    : request.SiteName.Trim(),
                Author: string.IsNullOrWhiteSpace(request.Author) ? null : request.Author.Trim(),
                Description: string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                Collections: collections), context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status201Created;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new
            {
                id = item.Id,
                title = item.Title,
                offline = request.SaveOffline,
            }, JsonOptions, context.RequestAborted);
        }
        catch (JsonException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message);
        }
        catch (ArgumentException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message);
        }
        catch (InvalidDataException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message);
        }
        finally
        {
            if (renderedPath is not null)
            {
                DeleteIfExists(renderedPath);
            }
        }
    }

    private async Task HandleSyncAsync(HttpContext context)
    {
        if (!Authenticate(context))
        {
            return;
        }

        if (context.Request.ContentLength is > MaximumPackageBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        await _syncGate.WaitAsync(context.RequestAborted);
        string? incoming = null;
        string? outgoing = null;
        try
        {
            var stagingRoot = Path.Combine(_profileRoot, "staging");
            Directory.CreateDirectory(stagingRoot);
            incoming = Path.Combine(stagingRoot, $"direct-in-{Guid.NewGuid():N}.partial");
            outgoing = Path.Combine(stagingRoot, $"direct-out-{Guid.NewGuid():N}.partial");
            await using (var output = new FileStream(
                incoming, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await CopyWithLimitAsync(context.Request.Body, output, MaximumPackageBytes, context.RequestAborted);
                await output.FlushAsync(context.RequestAborted);
                output.Flush(flushToDisk: true);
            }

            var imported = await _merge.ImportAsync(incoming, context.RequestAborted);
            var clientBlobs = await ReadAvailableBlobHashesAsync(incoming, context.RequestAborted);
            var localBlobs = await ReadLocalAssetHashesAsync(context.RequestAborted);
            var responseBlobs = new HashSet<string>(localBlobs, StringComparer.Ordinal);
            responseBlobs.ExceptWith(clientBlobs);
            var deltaRequested = string.Equals(
                context.Request.Headers["X-UniShare-Delta"], "1", StringComparison.Ordinal);
            var serverCursor = ParseCursor(context.Request.Headers["X-UniShare-Server-Cursor"]);
            var clientThrough = ParseCursor(context.Request.Headers["X-UniShare-Client-Through"]);
            var delta = await ReadSyncCursorStateAsync(
                deltaRequested && serverCursor.HasValue ? serverCursor : null,
                context.RequestAborted);
            var sendDelta = deltaRequested && serverCursor.HasValue &&
                serverCursor.Value <= delta.Through && _backup is IDeltaBackupService;
            BackupSummary exported;
            if (sendDelta && _backup is IDeltaBackupService deltaBackup)
            {
                exported = await deltaBackup.ExportDeltaAsync(
                    outgoing, delta.ItemIds, responseBlobs, localBlobs, context.RequestAborted);
            }
            else if (_backup is ISelectiveBackupService selective)
            {
                exported = await selective.ExportAsync(
                    outgoing, responseBlobs, localBlobs, context.RequestAborted);
            }
            else
            {
                exported = await _backup.ExportAsync(outgoing, context.RequestAborted);
            }
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/vnd.unishare.sync+zip";
            context.Response.ContentLength = new FileInfo(outgoing).Length;
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-UniShare-Imported"] = imported.ImportedItems.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers["X-UniShare-Updated"] = imported.UpdatedItems.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers["X-UniShare-Conflicts"] = imported.Conflicts.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers["X-UniShare-Items"] = exported.ItemCount.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers["X-UniShare-Blobs"] = exported.BlobCount.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers["X-UniShare-Revision"] = ReadRevision();
            context.Response.Headers["X-UniShare-Protocol"] = "2";
            context.Response.Headers["X-UniShare-Server-Device"] = delta.DeviceId.ToString("D");
            context.Response.Headers["X-UniShare-Full-Snapshot"] = sendDelta ? "0" : "1";
            context.Response.Headers["X-UniShare-Server-Through"] =
                delta.Through.ToString(CultureInfo.InvariantCulture);
            if (clientThrough.HasValue)
            {
                context.Response.Headers["X-UniShare-Client-Ack"] =
                    clientThrough.Value.ToString(CultureInfo.InvariantCulture);
            }
            await context.Response.SendFileAsync(outgoing, context.RequestAborted);
        }
        catch (InvalidDataException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status409Conflict, exception.Message);
        }
        finally
        {
            if (incoming is not null)
            {
                DeleteIfExists(incoming);
            }
            if (outgoing is not null)
            {
                DeleteIfExists(outgoing);
            }
            _syncGate.Release();
        }
    }

    private async Task HandleMissingBlobsAsync(HttpContext context)
    {
        if (!Authenticate(context))
        {
            return;
        }
        var bodySize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false })
        {
            bodySize.MaxRequestBodySize = MaximumInventoryBytes;
        }
        if (context.Request.ContentLength is > MaximumInventoryBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        try
        {
            var request = await context.Request.ReadFromJsonAsync<BlobInventoryRequest>(
                JsonOptions, context.RequestAborted) ??
                throw new InvalidDataException("El inventario de blobs está vacío.");
            var hashes = request.Hashes ?? [];
            if (hashes.Length > MaximumInventoryHashes)
            {
                throw new InvalidDataException("El inventario contiene demasiados blobs.");
            }

            var normalized = hashes.Distinct(StringComparer.Ordinal).ToArray();
            if (normalized.Any(hash => !IsValidHash(hash)))
            {
                throw new InvalidDataException("El inventario contiene un SHA-256 inválido.");
            }

            var missing = normalized.Where(hash => !LocalBlobExists(hash)).Order(StringComparer.Ordinal).ToArray();
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { missing }, JsonOptions, context.RequestAborted);
        }
        catch (JsonException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message);
        }
        catch (InvalidDataException exception)
        {
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, exception.Message);
        }
    }

    private async Task<HashSet<string>> ReadLocalAssetHashesAsync(CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT lower(sha256) FROM assets ORDER BY lower(sha256);";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var hash = reader.GetString(0);
            if (!IsValidHash(hash))
            {
                throw new InvalidDataException("La biblioteca contiene un SHA-256 de blob inválido.");
            }
            result.Add(hash);
        }
        return result;
    }

    private static async Task<HashSet<string>> ReadAvailableBlobHashesAsync(
        string packagePath,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry("sync/available_blobs.ndjson") ??
            archive.GetEntry("metadata/assets.ndjson") ??
            throw new InvalidDataException("El paquete no contiene inventario de blobs.");
        var result = new HashSet<string>(StringComparer.Ordinal);
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("sha256", out var property))
            {
                throw new InvalidDataException("El inventario no contiene sha256.");
            }
            var hash = property.GetString();
            if (hash is null || !IsValidHash(hash))
            {
                throw new InvalidDataException("El inventario contiene un SHA-256 inválido.");
            }
            result.Add(hash);
            if (result.Count > MaximumInventoryHashes)
            {
                throw new InvalidDataException("El inventario contiene demasiados blobs.");
            }
        }
        return result;
    }

    private bool LocalBlobExists(string hash) => File.Exists(Path.Combine(
        _profileRoot, "blobs", hash[..2], hash.Substring(2, 2), hash));

    private static bool IsValidHash(string hash) =>
        hash.Length == 64 && hash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private bool Authenticate(HttpContext context)
    {
        var supplied = context.Request.Headers.Authorization.ToString();
        const string scheme = "Bearer ";
        if (!supplied.StartsWith(scheme, StringComparison.OrdinalIgnoreCase) ||
            !FixedTimeEquals(supplied[scheme.Length..].Trim(), Configuration.PairingKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return false;
        }

        return true;
    }

    private DirectSyncConfiguration LoadOrCreateConfiguration(int? requestedPort)
    {
        var configRoot = Path.Combine(_profileRoot, "config");
        var path = Path.Combine(configRoot, "direct-sync.json");
        if (File.Exists(path))
        {
            var current = JsonSerializer.Deserialize<DirectSyncConfiguration>(File.ReadAllText(path), JsonOptions);
            if (current is not null && current.Port is >= 1024 and <= 65535 && current.PairingKey.Length >= 32)
            {
                return current;
            }
            throw new InvalidDataException("La configuración de sincronización directa está dañada.");
        }

        if (requestedPort is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedPort));
        }

        Directory.CreateDirectory(configRoot);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var created = new DirectSyncConfiguration(requestedPort ?? 47831, token);
        SaveConfiguration(created, overwrite: false);
        return created;
    }

    private void SaveConfiguration(DirectSyncConfiguration configuration, bool overwrite = true)
    {
        var configRoot = Path.Combine(_profileRoot, "config");
        Directory.CreateDirectory(configRoot);
        var path = Path.Combine(configRoot, "direct-sync.json");
        var temporary = path + ".partial";
        File.WriteAllText(temporary, JsonSerializer.Serialize(configuration, JsonOptions));
        File.Move(temporary, path, overwrite);
    }

    private string ReadRevision()
    {
        static string Part(string path) => File.Exists(path)
            ? $"{new FileInfo(path).Length}:{File.GetLastWriteTimeUtc(path).Ticks}"
            : "0:0";
        var raw = $"{Part(_databasePath)}|{Part(_databasePath + "-wal")}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private async Task<SyncCursorState> ReadSyncCursorStateAsync(
        long? afterExclusive,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var identity = connection.CreateCommand();
        identity.CommandText = """
            SELECT
                (SELECT value FROM settings WHERE key='device_id'),
                CAST((SELECT value FROM settings WHERE key='next_counter') AS INTEGER);
            """;
        await using var identityReader = await identity.ExecuteReaderAsync(cancellationToken);
        if (!await identityReader.ReadAsync(cancellationToken))
        {
            throw new InvalidDataException("La biblioteca no tiene identidad de sincronización.");
        }

        var deviceId = Guid.Parse(identityReader.GetString(0));
        var through = identityReader.GetInt64(1);
        var itemIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (afterExclusive.HasValue)
        {
            await identityReader.DisposeAsync();
            await using var operations = connection.CreateCommand();
            operations.CommandText = """
                SELECT DISTINCT entity_id FROM operations
                WHERE origin_device_id=$device AND origin_counter>$after AND origin_counter<=$through
                  AND entity_type='item'
                ORDER BY entity_id;
                """;
            operations.Parameters.AddWithValue("$device", deviceId.ToString("D"));
            operations.Parameters.AddWithValue("$after", afterExclusive.Value);
            operations.Parameters.AddWithValue("$through", through);
            await using var reader = await operations.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                itemIds.Add(Guid.Parse(reader.GetString(0)).ToString("D"));
            }
        }

        return new SyncCursorState(deviceId, through, itemIds);
    }

    private static long? ParseCursor(string? value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static async Task CopyWithLimitAsync(
        Stream input,
        Stream output,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                return;
            }
            total += count;
            if (total > maximumBytes)
            {
                throw new InvalidDataException("El paquete directo supera el límite de 4 GiB.");
            }
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string detail)
    {
        if (context.Response.HasStarted)
        {
            return;
        }
        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new { status = statusCode, detail }, context.RequestAborted);
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Staging is cleaned on the next normal profile maintenance pass.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_application is not null)
        {
            await _application.StopAsync().ConfigureAwait(false);
            await _application.DisposeAsync().ConfigureAwait(false);
            _application = null;
        }
        _syncGate.Dispose();
    }

    private sealed record SyncCursorState(Guid DeviceId, long Through, HashSet<string> ItemIds);
}

internal sealed record BrowserCaptureRequest(
    string? Url,
    string? Title,
    string? Collection,
    bool SaveOffline = false,
    string? RenderedText = null,
    string? SiteName = null,
    string? Author = null,
    string? Description = null,
    RenderedPageImage[]? Images = null);

internal sealed record BlobInventoryRequest(string[]? Hashes);
