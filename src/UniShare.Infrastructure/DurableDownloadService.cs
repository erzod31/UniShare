using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using UniShare.Application;
using UniShare.Domain;

namespace UniShare.Infrastructure;

public sealed class DurableDownloadService : IDownloadService, IDisposable
{
    private const int MaximumRedirects = 5;
    private const int MaximumMetadataBytes = 2 * 1024 * 1024;
    private const long ProgressIntervalBytes = 1024 * 1024;
    private readonly IDownloadJobRepository _repository;
    private readonly IBlobStore _blobStore;
    private readonly IRemoteEndpointPolicy _endpointPolicy;
    private readonly TimeProvider _timeProvider;
    private readonly HttpClient _httpClient;
    private readonly string _stagingRoot;
    private readonly long _maximumBytes;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public DurableDownloadService(
        IDownloadJobRepository repository,
        IBlobStore blobStore,
        IRemoteEndpointPolicy endpointPolicy,
        string profileRoot,
        TimeProvider timeProvider,
        long maximumBytes = 1024L * 1024 * 1024,
        HttpMessageHandler? handler = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        _repository = repository;
        _blobStore = blobStore;
        _endpointPolicy = endpointPolicy;
        _timeProvider = timeProvider;
        _maximumBytes = maximumBytes;
        _stagingRoot = Path.Combine(Path.GetFullPath(profileRoot), "staging");
        Directory.CreateDirectory(_stagingRoot);
        handler ??= new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            UseProxy = false,
            ConnectCallback = ConnectValidatedAsync,
        };
        _httpClient = new HttpClient(handler, disposeHandler: handler is SocketsHttpHandler)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("UniShare/0.1");
    }

    public Task<DownloadJob> QueueAsync(
        Guid itemId,
        Uri sourceUri,
        CancellationToken cancellationToken = default) =>
        _repository.QueueDownloadAsync(itemId, sourceUri, _timeProvider.GetUtcNow(), cancellationToken);

    public async Task<WebCaptureMetadata?> FetchMetadataAsync(
        Uri sourceUri,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var request = new DownloadJob(
            Guid.NewGuid(), Guid.Empty, sourceUri, DownloadJobState.Queued, 0, null, null, null,
            null, null, null, now, now);
        using var response = await SendFollowingRedirectsAsync(request, 0, cancellationToken);
        var mediaType = NormalizeMimeType(response.Content.Headers.ContentType);
        if (mediaType is not ("text/html" or "application/xhtml+xml"))
        {
            return null;
        }

        if (response.Content.Headers.ContentLength is > MaximumMetadataBytes)
        {
            return null;
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[32 * 1024];
        while (output.Length <= MaximumMetadataBytes)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (output.Length > MaximumMetadataBytes)
        {
            return null;
        }

        var html = Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
        return SafeHtmlSnapshot.ExtractMetadata(html, response.RequestMessage?.RequestUri ?? sourceUri);
    }

    public async Task<DownloadJob> RunAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _repository.GetDownloadAsync(jobId, cancellationToken) ??
            throw new KeyNotFoundException("La descarga no existe.");
        if (!job.CanRun)
        {
            throw new InvalidOperationException($"La descarga no puede iniciarse desde el estado {job.State}.");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!_running.TryAdd(jobId, linked))
        {
            throw new InvalidOperationException("La descarga ya está en ejecución.");
        }

        var token = linked.Token;
        var partialPath = GetPartialPath(jobId);
        try
        {
            var offset = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            if (offset > 0 && string.IsNullOrWhiteSpace(job.ETag) && !job.LastModified.HasValue)
            {
                File.Delete(partialPath);
                offset = 0;
            }

            await _repository.UpdateDownloadAsync(
                jobId, DownloadJobState.Running, offset, job.TotalBytes, job.ETag, job.LastModified,
                null, null, _timeProvider.GetUtcNow(), token);
            using var response = await SendFollowingRedirectsAsync(job, offset, token);
            var append = offset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (append && response.Content.Headers.ContentRange?.From != offset)
            {
                throw new InvalidDataException("El servidor devolvió un rango distinto al solicitado.");
            }

            var responseEtag = response.Headers.ETag?.ToString();
            if (append && !string.IsNullOrWhiteSpace(job.ETag) &&
                !string.Equals(job.ETag, responseEtag, StringComparison.Ordinal))
            {
                File.Delete(partialPath);
                throw new InvalidDataException("El recurso cambió durante la reanudación; se reiniciará al reintentar.");
            }

            if (append && string.IsNullOrWhiteSpace(job.ETag) && job.LastModified.HasValue &&
                response.Content.Headers.LastModified != job.LastModified)
            {
                File.Delete(partialPath);
                throw new InvalidDataException("El recurso cambió durante la reanudación; se reiniciará al reintentar.");
            }

            if (!append)
            {
                offset = 0;
            }

            var totalBytes = response.Content.Headers.ContentRange?.Length ??
                (response.Content.Headers.ContentLength.HasValue
                    ? offset + response.Content.Headers.ContentLength.Value
                    : null);
            if (totalBytes > _maximumBytes)
            {
                throw new InvalidDataException("La descarga supera el límite configurado.");
            }

            var etag = responseEtag;
            var lastModified = response.Content.Headers.LastModified;
            await _repository.UpdateDownloadAsync(
                jobId, DownloadJobState.Running, offset, totalBytes, etag, lastModified,
                null, null, _timeProvider.GetUtcNow(), token);

            var receivedBytes = offset;
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var destination = new FileStream(
                partialPath,
                append ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                var lastReported = receivedBytes;
                int read;
                while ((read = await source.ReadAsync(buffer, token)) > 0)
                {
                    receivedBytes += read;
                    if (receivedBytes > _maximumBytes)
                    {
                        throw new InvalidDataException("La descarga supera el límite configurado.");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), token);
                    if (receivedBytes - lastReported >= ProgressIntervalBytes)
                    {
                        await _repository.UpdateDownloadAsync(
                            jobId, DownloadJobState.Running, receivedBytes, totalBytes, etag, lastModified,
                            null, null, _timeProvider.GetUtcNow(), token);
                        lastReported = receivedBytes;
                    }
                }

                await destination.FlushAsync(token);
                destination.Flush(flushToDisk: true);
            }

            if (totalBytes.HasValue && receivedBytes != totalBytes.Value)
            {
                throw new InvalidDataException("La descarga terminó con un tamaño distinto al anunciado.");
            }

            var finalUri = response.RequestMessage?.RequestUri ?? job.SourceUri;
            var fileName = GetFileName(finalUri, jobId);
            var detectedMimeType = await ContentTypeDetector.DetectFileAsync(partialPath, token);
            var declaredMimeType = NormalizeMimeType(response.Content.Headers.ContentType) ??
                MimeTypes.FromFileName(fileName);
            var mimeType = detectedMimeType ?? declaredMimeType;
            WebCaptureMetadata? metadata = null;
            if (IsActiveWebContent(fileName, mimeType, declaredMimeType))
            {
                metadata = await SafeHtmlSnapshot.RewriteAsync(partialPath, finalUri, token);
                mimeType = "text/html";
                fileName = GetSnapshotFileName(fileName, jobId);
            }

            var imported = await _blobStore.ImportFileAsync(partialPath, token);
            var asset = new AssetManifest(
                Guid.NewGuid(), job.ItemId, imported.Sha256, imported.ByteLength, mimeType,
                fileName, imported.RelativePath, _timeProvider.GetUtcNow());
            await _repository.CompleteDownloadAsync(jobId, asset, metadata, _timeProvider.GetUtcNow(), token);
            File.Delete(partialPath);
            return await _repository.GetDownloadAsync(jobId, token) ??
                throw new InvalidOperationException("No se pudo leer la descarga completada.");
        }
        catch (OperationCanceledException)
        {
            var current = await _repository.GetDownloadAsync(jobId, CancellationToken.None);
            if (current?.State == DownloadJobState.Cancelled)
            {
                if (File.Exists(partialPath))
                {
                    File.Delete(partialPath);
                }
            }
            else
            {
                var bytes = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
                await _repository.UpdateDownloadAsync(
                    jobId, DownloadJobState.Paused, bytes, current?.TotalBytes, current?.ETag,
                    current?.LastModified, "cancelled", "Transferencia pausada.",
                    _timeProvider.GetUtcNow(), CancellationToken.None);
            }

            throw;
        }
        catch (Exception exception)
        {
            var current = await _repository.GetDownloadAsync(jobId, CancellationToken.None);
            if (current?.State != DownloadJobState.Cancelled)
            {
                var bytes = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
                await _repository.UpdateDownloadAsync(
                    jobId, DownloadJobState.Failed, bytes, current?.TotalBytes, current?.ETag,
                    current?.LastModified, exception.GetType().Name, "La transferencia falló; puedes reintentar.",
                    _timeProvider.GetUtcNow(), CancellationToken.None);
            }
            else if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }

            throw;
        }
        finally
        {
            _running.TryRemove(jobId, out _);
        }
    }

    public async Task<DownloadJob> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _repository.GetDownloadAsync(jobId, cancellationToken) ??
            throw new KeyNotFoundException("La descarga no existe.");
        if (job.State == DownloadJobState.Completed)
        {
            return job;
        }

        var partialPath = GetPartialPath(jobId);
        var bytes = File.Exists(partialPath) ? new FileInfo(partialPath).Length : job.BytesReceived;
        await _repository.UpdateDownloadAsync(
            jobId, DownloadJobState.Cancelled, bytes, job.TotalBytes, job.ETag, job.LastModified,
            "user_cancelled", "Descarga cancelada por el usuario.", _timeProvider.GetUtcNow(), cancellationToken);
        if (_running.TryGetValue(jobId, out var source))
        {
            source.Cancel();
        }
        else if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }

        return await _repository.GetDownloadAsync(jobId, cancellationToken) ??
            throw new InvalidOperationException("No se pudo leer la descarga cancelada.");
    }

    public Task<IReadOnlyList<DownloadJob>> ListAsync(CancellationToken cancellationToken = default) =>
        _repository.ListDownloadsAsync(cancellationToken);

    public void Dispose()
    {
        foreach (var source in _running.Values)
        {
            source.Cancel();
            source.Dispose();
        }

        _httpClient.Dispose();
    }

    private async Task<HttpResponseMessage> SendFollowingRedirectsAsync(
        DownloadJob job,
        long offset,
        CancellationToken cancellationToken)
    {
        var uri = job.SourceUri;
        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            await _endpointPolicy.ResolveAndValidateAsync(uri, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (offset > 0)
            {
                request.Headers.Range = new RangeHeaderValue(offset, null);
                if (EntityTagHeaderValue.TryParse(job.ETag, out var etag))
                {
                    request.Headers.IfRange = new RangeConditionHeaderValue(etag);
                }
                else if (job.LastModified.HasValue)
                {
                    request.Headers.IfRange = new RangeConditionHeaderValue(job.LastModified.Value);
                }
            }

            var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!IsRedirect(response.StatusCode))
            {
                response.EnsureSuccessStatusCode();
                return response;
            }

            var location = response.Headers.Location ?? throw new InvalidDataException(
                "La redirección no incluye destino.");
            var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
            response.Dispose();
            uri = next;
        }

        throw new InvalidDataException("La descarga superó el máximo de redirecciones.");
    }

    private string GetPartialPath(Guid jobId) => Path.Combine(_stagingRoot, $"download-{jobId:N}.partial");

    private async ValueTask<Stream> ConnectValidatedAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var uri = context.InitialRequestMessage.RequestUri ??
            throw new InvalidOperationException("La petición no incluye una URI remota.");
        var addresses = await _endpointPolicy.ResolveAndValidateAsync(uri, cancellationToken);
        SocketException? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException exception)
            {
                lastError = exception;
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException("No se pudo conectar con ninguna dirección remota validada.", lastError);
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static string GetFileName(Uri uri, Guid jobId)
    {
        var name = Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath));
        return string.IsNullOrWhiteSpace(name) ? $"download-{jobId:N}" : name;
    }

    private static string GetSnapshotFileName(string fileName, Guid jobId)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(stem)
            ? $"snapshot-{jobId:N}.offline.html"
            : $"{stem}.offline.html";
    }

    private static bool IsActiveWebContent(string fileName, string detectedMimeType, string declaredMimeType)
    {
        if (detectedMimeType is "text/html" or "image/svg+xml" or "application/xhtml+xml" ||
            declaredMimeType is "text/html" or "image/svg+xml" or "application/xhtml+xml")
        {
            return true;
        }

        return Path.GetExtension(fileName).ToLowerInvariant() is ".html" or ".htm" or ".xhtml" or ".svg";
    }

    private static string? NormalizeMimeType(MediaTypeHeaderValue? contentType)
    {
        var value = contentType?.MediaType;
        return string.IsNullOrWhiteSpace(value) || value.Length > 200 ? null : value.ToLowerInvariant();
    }
}
