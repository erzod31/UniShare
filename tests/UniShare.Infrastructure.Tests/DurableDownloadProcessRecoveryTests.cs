using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using UniShare.Application;
using UniShare.Domain;
using UniShare.Infrastructure;

namespace UniShare.Infrastructure.Tests;

public sealed class DurableDownloadProcessRecoveryTests
{
    private const string EntityTag = "\"crash-v1\"";
    private const int PayloadLength = 8 * 1024 * 1024;
    private const int EarlyKillThreshold = 256 * 1024;
    private const int CheckpointedKillThreshold = 1536 * 1024;

    [Theory]
    [InlineData(EarlyKillThreshold)]
    [InlineData(CheckpointedKillThreshold)]
    public async Task KilledProcessAtTransferCheckpointsResumesToExactBlob(int killThreshold)
    {
        using var temporary = new TemporaryDirectory();
        var profile = Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var payload = CreatePayload(PayloadLength);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var source = new Uri($"http://download-crash.invalid:{endpoint.Port}/crash.bin");
        var item = LibraryItem.Create(ItemKind.Link, "Corte real", source.AbsoluteUri, DateTimeOffset.UtcNow);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);
        var queued = await repository.QueueDownloadAsync(
            item.Id, source, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        var partialPath = Path.Combine(profile, "staging", $"download-{queued.Id:N}.partial");
        using var child = StartHarness(profile, queued.Id);
        var server = ServeSlowResponseAsync(listener, payload);

        try
        {
            await WaitForDurablePartialAsync(child, repository, queued.Id, partialPath, killThreshold);
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(TestContext.Current.CancellationToken);
            await server.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            var interrupted = await repository.GetDownloadAsync(
                queued.Id, TestContext.Current.CancellationToken);
            Assert.NotNull(interrupted);
            Assert.Equal(DownloadJobState.Running, interrupted.State);
            Assert.Equal(EntityTag, interrupted.ETag);
            var persistedBytes = new FileInfo(partialPath).Length;
            Assert.InRange(persistedBytes, killThreshold, PayloadLength - 1);
            Assert.Empty(await repository.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));

            var resumedRequestSeen = false;
            var handler = new DelegateHandler((request, _) =>
            {
                var offset = request.Headers.Range?.Ranges.Single().From;
                Assert.Equal(persistedBytes, offset);
                Assert.Equal(EntityTag, request.Headers.IfRange?.EntityTag?.Tag);
                resumedRequestSeen = true;
                var remaining = payload.AsMemory(checked((int)persistedBytes)).ToArray();
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                {
                    RequestMessage = request,
                    Content = new ByteArrayContent(remaining),
                };
                response.Headers.ETag = new EntityTagHeaderValue(EntityTag);
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                    persistedBytes, PayloadLength - 1, PayloadLength);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                return response;
            });
            var blobStore = new FileBlobStore(profile);
            using var downloads = new DurableDownloadService(
                repository, blobStore, new PermissivePolicy(), profile, TimeProvider.System, handler: handler);

            var completed = await downloads.RunAsync(queued.Id, TestContext.Current.CancellationToken);

            Assert.True(resumedRequestSeen);
            Assert.Equal(DownloadJobState.Completed, completed.State);
            Assert.False(File.Exists(partialPath));
            var asset = Assert.Single(await repository.GetAssetsAsync(
                item.Id, TestContext.Current.CancellationToken));
            Assert.Equal(PayloadLength, asset.ByteLength);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), asset.Sha256);
            Assert.Equal(
                payload,
                await File.ReadAllBytesAsync(
                    blobStore.GetAbsolutePath(asset.RelativePath), TestContext.Current.CancellationToken));
        }
        finally
        {
            listener.Stop();
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private static Process StartHarness(string profile, Guid jobId)
    {
        var configuration = Directory.GetParent(AppContext.BaseDirectory)?.Parent?.Name ?? "Release";
        var root = FindRepositoryRoot();
        var executableName = OperatingSystem.IsWindows()
            ? "UniShare.DownloadCrashHarness.exe"
            : "UniShare.DownloadCrashHarness";
        var executable = Path.Combine(
            root, "tests", "UniShare.DownloadCrashHarness", "bin", configuration, "net10.0", executableName);
        Assert.True(File.Exists(executable), $"No se encontró el arnés de corte: {executable}");
        var start = new ProcessStartInfo(executable)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(profile);
        start.ArgumentList.Add(jobId.ToString("D"));
        return Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar el arnés de corte.");
    }

    private static async Task WaitForDurablePartialAsync(
        Process child,
        SqliteLibraryRepository repository,
        Guid jobId,
        string partialPath,
        int killThreshold)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!timeout.IsCancellationRequested)
        {
            if (child.HasExited)
            {
                var standardOutput = await child.StandardOutput.ReadToEndAsync(timeout.Token);
                var standardError = await child.StandardError.ReadToEndAsync(timeout.Token);
                Assert.Fail(
                    $"El arnés terminó antes del corte (exit {child.ExitCode}). stdout={standardOutput} stderr={standardError}");
            }

            var current = await repository.GetDownloadAsync(jobId, timeout.Token);
            if (current is { State: DownloadJobState.Running, ETag: EntityTag } &&
                File.Exists(partialPath) && new FileInfo(partialPath).Length >= killThreshold)
            {
                return;
            }

            await Task.Delay(25, timeout.Token);
        }

        Assert.Fail("La descarga no alcanzó un punto durable antes del tiempo límite.");
    }

    private static async Task ServeSlowResponseAsync(TcpListener listener, byte[] payload)
    {
        try
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(
                stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
            {
            }

            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Length: {payload.Length}\r\n" +
                $"ETag: {EntityTag}\r\nAccept-Ranges: bytes\r\n" +
                "Content-Type: application/octet-stream\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers);
            const int chunkSize = 64 * 1024;
            for (var offset = 0; offset < payload.Length; offset += chunkSize)
            {
                var count = Math.Min(chunkSize, payload.Length - offset);
                await stream.WriteAsync(payload.AsMemory(offset, count));
                await stream.FlushAsync();
                await Task.Delay(20);
            }
        }
        catch (IOException)
        {
            // El cierre abrupto del socket es precisamente el fallo simulado.
        }
        catch (SocketException)
        {
            // El proceso hijo fue terminado durante la transferencia.
        }
    }

    private static byte[] CreatePayload(int length)
    {
        var payload = new byte[length];
        for (var index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)((index * 31 + 17) % 251);
        }

        return payload;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "UniShare.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("No se encontró la raíz del repositorio.");
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(callback(request, cancellationToken));
    }

    private sealed class PermissivePolicy : IRemoteEndpointPolicy
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAndValidateAsync(
            Uri uri,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Loopback]);
    }
}
