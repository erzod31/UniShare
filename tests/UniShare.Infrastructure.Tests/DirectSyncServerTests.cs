using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using UniShare.Application;
using UniShare.Domain;

namespace UniShare.Infrastructure.Tests;

public sealed class DirectSyncServerTests
{
    [Fact]
    public async Task PublicTailscaleEndpointIsValidatedAndPersistsWithoutChangingThePairingKey()
    {
        using var temporary = new TemporaryDirectory();
        var profile = await CreateProfileAsync(temporary.Path, "configuration");
        var port = ReservePort();
        string originalKey;

        await using (var server = new DirectSyncServer(profile.Root, profile.Backup, profile.Sync, port))
        {
            originalKey = server.Configuration.PairingKey;
            server.UpdatePublicEndpoint("  https://computer.example.ts.net:8443/  ");
            Assert.Equal("https://computer.example.ts.net:8443", server.Configuration.PublicEndpoint);
            Assert.Throws<ArgumentException>(() => server.UpdatePublicEndpoint("http://computer.example.ts.net:8443"));
            Assert.Throws<ArgumentException>(() => server.UpdatePublicEndpoint("https://example.com"));
            Assert.Throws<ArgumentException>(() => server.UpdatePublicEndpoint("https://computer.example.ts.net:8443/private"));
        }

        await using var reloaded = new DirectSyncServer(profile.Root, profile.Backup, profile.Sync);
        Assert.Equal(originalKey, reloaded.Configuration.PairingKey);
        Assert.Equal("https://computer.example.ts.net:8443", reloaded.Configuration.PublicEndpoint);
    }

    [Fact]
    public async Task AuthenticatedRoundTripTransfersChangesBothWaysAndRejectsUnknownClients()
    {
        using var temporary = new TemporaryDirectory();
        var windows = await CreateProfileAsync(temporary.Path, "windows");
        var android = await CreateProfileAsync(temporary.Path, "android");
        await android.Library.CreateAsync(new CreateItemRequest(
            "Creado en Android", "https://example.com/android", null), TestContext.Current.CancellationToken);

        var port = ReservePort();
        await using var server = new DirectSyncServer(windows.Root, windows.Backup, windows.Sync, port);
        await server.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(server.Configuration.LocalAddress) };

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/status", TestContext.Current.CancellationToken)).StatusCode);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", server.Configuration.PairingKey);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/status", TestContext.Current.CancellationToken)).StatusCode);

        var responsePackage = await ExchangeAsync(client, android, temporary.Path, "first");
        Assert.Equal("Creado en Android", Assert.Single(await windows.Library.SearchAsync(null, cancellationToken: TestContext.Current.CancellationToken)).Title);
        await android.Sync.ImportAsync(responsePackage, TestContext.Current.CancellationToken);

        await windows.Library.CreateAsync(new CreateItemRequest(
            "Creado en Windows", "https://example.com/windows", null), TestContext.Current.CancellationToken);
        responsePackage = await ExchangeAsync(client, android, temporary.Path, "second");
        await android.Sync.ImportAsync(responsePackage, TestContext.Current.CancellationToken);

        Assert.Equal(2, (await windows.Library.SearchAsync(null, cancellationToken: TestContext.Current.CancellationToken)).Count);
        Assert.Equal(2, (await android.Library.SearchAsync(null, cancellationToken: TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task AuthenticatedBrowserCaptureCreatesDurableOrganizedLink()
    {
        using var temporary = new TemporaryDirectory();
        var profile = await CreateProfileAsync(temporary.Path, "browser");
        var port = ReservePort();
        await using var server = new DirectSyncServer(
            profile.Root, profile.Backup, profile.Sync, port, profile.Library);
        await server.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(server.Configuration.LocalAddress) };

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/capture", new { url = "https://example.com/private", title = "Privado" }, cancellationToken: TestContext.Current.CancellationToken)).StatusCode);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", server.Configuration.PairingKey);
        using var response = await client.PostAsJsonAsync("/api/v1/capture", new { url = "https://example.com/article", title = "Artículo", collection = "Lecturas" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = Assert.Single(await profile.Library.SearchAsync("Artículo", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("https://example.com/article", item.OriginalUrl);
        Assert.Equal("Extensión Chromium", item.Source);
        Assert.Equal("Lecturas", Assert.Single((await profile.Library.GetOrganizationAsync(item.Id, TestContext.Current.CancellationToken)).Collections).Name);
    }

    [Fact]
    public async Task AuthenticatedRenderedBrowserCaptureCreatesInertHybridWithEmbeddedImage()
    {
        using var temporary = new TemporaryDirectory();
        var profile = await CreateProfileAsync(temporary.Path, "rendered-browser");
        var port = ReservePort();
        await using var server = new DirectSyncServer(
            profile.Root, profile.Backup, profile.Sync, port, profile.Library);
        await server.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(server.Configuration.LocalAddress) };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", server.Configuration.PairingKey);
        var pixel = Convert.ToBase64String([
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        ]);

        using var response = await client.PostAsJsonAsync("/api/v1/capture", new
        {
            url = "https://example.com/app",
            title = "Aplicación renderizada",
            collection = "Lecturas dinámicas",
            saveOffline = true,
            renderedText = "Contenido creado por JavaScript <script>alert(1)</script>",
            siteName = "Example App",
            author = "Autora",
            description = "Descripción renderizada",
            images = new[]
                {
                    new { dataUrl = $"data:image/png;base64,{pixel}", alternativeText = "Vista principal" },
                },
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var item = Assert.Single(await profile.Library.SearchAsync("Aplicación renderizada", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(ItemKind.Hybrid, item.Kind);
        Assert.Equal("Example App", item.Source);
        Assert.Equal("Autora", item.Author);
        Assert.Equal("Descripción renderizada", item.Description);
        var asset = Assert.Single(await profile.Library.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));
        var snapshot = await File.ReadAllTextAsync(profile.BlobStore.GetAbsolutePath(asset.RelativePath), TestContext.Current.CancellationToken);
        Assert.Contains("Contenido creado por JavaScript", snapshot, StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,", snapshot, StringComparison.Ordinal);
        Assert.Contains("Vista principal", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", snapshot, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("script-src 'none'", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlobNegotiationReportsOnlyContentMissingOnWindows()
    {
        using var temporary = new TemporaryDirectory();
        var profile = await CreateProfileAsync(temporary.Path, "blob-plan");
        var source = Path.Combine(temporary.Path, "known.bin");
        await File.WriteAllBytesAsync(source, Encoding.UTF8.GetBytes("contenido conocido"), TestContext.Current.CancellationToken);
        var item = await profile.Library.CreateAsync(new CreateItemRequest("Conocido", null, source), TestContext.Current.CancellationToken);
        var known = Assert.Single(await profile.Library.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken)).Sha256;
        var missing = new string('a', 64);
        var port = ReservePort();
        await using var server = new DirectSyncServer(profile.Root, profile.Backup, profile.Sync, port);
        await server.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(server.Configuration.LocalAddress) };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", server.Configuration.PairingKey);

        using var response = await client.PostAsJsonAsync("/api/v1/blobs/missing", new { hashes = new[] { known, missing } }, cancellationToken: TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(missing, Assert.Single(body.RootElement.GetProperty("missing").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task StagingFailureDoesNotLeaveTheSynchronizationGateLocked()
    {
        using var temporary = new TemporaryDirectory();
        var profile = await CreateProfileAsync(temporary.Path, "blocked-staging");
        var staging = Path.Combine(profile.Root, "staging");
        Directory.Delete(staging);
        File.WriteAllText(staging, "not a directory");
        var port = ReservePort();
        await using var server = new DirectSyncServer(profile.Root, profile.Backup, profile.Sync, port);
        await server.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(server.Configuration.LocalAddress) };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", server.Configuration.PairingKey);

        using var first = await client.PostAsync(
            "/api/v1/sync", new ByteArrayContent([]), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var second = await client.PostAsync("/api/v1/sync", new ByteArrayContent([]), timeout.Token);
        Assert.Equal(HttpStatusCode.InternalServerError, second.StatusCode);
    }

    [Fact]
    public async Task SelectiveRoundTripTransfersOnlyBlobsAbsentFromTheOtherReplica()
    {
        using var temporary = new TemporaryDirectory();
        var windows = await CreateProfileAsync(temporary.Path, "selective-windows");
        var android = await CreateProfileAsync(temporary.Path, "selective-android");
        var androidFile = Path.Combine(temporary.Path, "android.bin");
        await File.WriteAllBytesAsync(androidFile, Encoding.UTF8.GetBytes("desde Android"), TestContext.Current.CancellationToken);
        var androidItem = await android.Library.CreateAsync(new CreateItemRequest("Android", null, androidFile), TestContext.Current.CancellationToken);
        var androidHash = Assert.Single(await android.Library.GetAssetsAsync(androidItem.Id, TestContext.Current.CancellationToken)).Sha256;
        var port = ReservePort();
        await using var server = new DirectSyncServer(windows.Root, windows.Backup, windows.Sync, port);
        await server.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(server.Configuration.LocalAddress) };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", server.Configuration.PairingKey);

        var firstRequest = Path.Combine(temporary.Path, "selective-first-request.zip");
        await android.Backup.ExportAsync(firstRequest, new HashSet<string> { androidHash }, new HashSet<string> { androidHash }, TestContext.Current.CancellationToken);
        var firstResponse = await PostPackageAsync(client, firstRequest, temporary.Path, "selective-first-response.zip");
        Assert.Equal("0", firstResponse.BlobHeader);
        await android.Sync.ImportAsync(firstResponse.Path, TestContext.Current.CancellationToken);

        var windowsFile = Path.Combine(temporary.Path, "windows.bin");
        await File.WriteAllBytesAsync(windowsFile, Encoding.UTF8.GetBytes("desde Windows"), TestContext.Current.CancellationToken);
        await windows.Library.CreateAsync(new CreateItemRequest("Windows", null, windowsFile), TestContext.Current.CancellationToken);
        var secondRequest = Path.Combine(temporary.Path, "selective-second-request.zip");
        await android.Backup.ExportAsync(secondRequest, new HashSet<string>(), new HashSet<string> { androidHash }, TestContext.Current.CancellationToken);
        var secondResponse = await PostPackageAsync(client, secondRequest, temporary.Path, "selective-second-response.zip");

        Assert.Equal("1", secondResponse.BlobHeader);
        Assert.Equal(1, (await android.Backup.InspectAsync(secondResponse.Path, TestContext.Current.CancellationToken)).BlobCount);
        await android.Sync.ImportAsync(secondResponse.Path, TestContext.Current.CancellationToken);
        Assert.Equal(2, (await android.Library.SearchAsync(null, cancellationToken: TestContext.Current.CancellationToken)).Count);
    }

    [Fact]
    public async Task DeltaCursorResendsUntilAcknowledgedThenReturnsOnlyNewOperations()
    {
        using var temporary = new TemporaryDirectory();
        var windows = await CreateProfileAsync(temporary.Path, "delta-windows");
        var android = await CreateProfileAsync(temporary.Path, "delta-android");
        _ = await windows.Library.CreateAsync(new CreateItemRequest(
            "Inicial Windows", "https://example.com/windows-initial", null),
            TestContext.Current.CancellationToken);
        _ = await android.Library.CreateAsync(new CreateItemRequest(
            "Inicial Android", "https://example.com/android-initial", null),
            TestContext.Current.CancellationToken);
        var port = ReservePort();
        await using var server = new DirectSyncServer(windows.Root, windows.Backup, windows.Sync, port);
        await server.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri(server.Configuration.LocalAddress) };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", server.Configuration.PairingKey);

        var initialRequest = Path.Combine(temporary.Path, "delta-initial-request.zip");
        await android.Backup.ExportAsync(initialRequest, TestContext.Current.CancellationToken);
        var initial = await PostDeltaAsync(client, initialRequest, temporary.Path, "delta-initial-response.zip", null, 1);
        Assert.Equal("1", initial.ClientAck);
        Assert.True(initial.FullSnapshot);
        Assert.True(initial.ServerThrough > 0);
        Assert.Equal(2, (await android.Sync.ImportAsync(initial.Path, TestContext.Current.CancellationToken)).ImportedItems + 1);

        _ = await windows.Library.CreateAsync(new CreateItemRequest(
            "Sólo delta", "https://example.com/windows-delta", null),
            TestContext.Current.CancellationToken);
        var emptyRequest = Path.Combine(temporary.Path, "delta-empty-request.zip");
        await android.Backup.ExportDeltaAsync(
            emptyRequest, new HashSet<string>(), new HashSet<string>(), new HashSet<string>(),
            TestContext.Current.CancellationToken);

        var delta = await PostDeltaAsync(
            client, emptyRequest, temporary.Path, "delta-response.zip", initial.ServerThrough, 1);
        Assert.False(delta.FullSnapshot);
        Assert.Equal(1, (await android.Backup.InspectAsync(delta.Path, TestContext.Current.CancellationToken)).ItemCount);
        await android.Sync.ImportAsync(delta.Path, TestContext.Current.CancellationToken);
        Assert.Contains(await android.Library.SearchAsync(null, cancellationToken: TestContext.Current.CancellationToken),
            item => item.Title == "Sólo delta");

        var retry = await PostDeltaAsync(
            client, emptyRequest, temporary.Path, "delta-retry.zip", initial.ServerThrough, 1);
        Assert.Equal(1, (await android.Backup.InspectAsync(retry.Path, TestContext.Current.CancellationToken)).ItemCount);
        var acknowledged = await PostDeltaAsync(
            client, emptyRequest, temporary.Path, "delta-acknowledged.zip", delta.ServerThrough, 1);
        Assert.Equal(0, (await android.Backup.InspectAsync(acknowledged.Path, TestContext.Current.CancellationToken)).ItemCount);

        var recovered = await PostDeltaAsync(
            client, emptyRequest, temporary.Path, "delta-recovered.zip", long.MaxValue, 1);
        Assert.True(recovered.FullSnapshot);
        Assert.Equal(3, (await android.Backup.InspectAsync(
            recovered.Path, TestContext.Current.CancellationToken)).ItemCount);
    }

    private static async Task<string> ExchangeAsync(
        HttpClient client,
        Profile sender,
        string root,
        string name)
    {
        var requestPackage = Path.Combine(root, $"{name}-request.zip");
        var responsePackage = Path.Combine(root, $"{name}-response.zip");
        await sender.Backup.ExportAsync(requestPackage);
        await using var requestStream = File.OpenRead(requestPackage);
        using var content = new StreamContent(requestStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.unishare.sync+zip");
        using var response = await client.PostAsync("/api/v1/sync", content);
        response.EnsureSuccessStatusCode();
        await using var output = File.Create(responsePackage);
        await response.Content.CopyToAsync(output);
        return responsePackage;
    }

    private static async Task<PackageResponse> PostPackageAsync(
        HttpClient client,
        string requestPackage,
        string root,
        string responseName)
    {
        await using var requestStream = File.OpenRead(requestPackage);
        using var content = new StreamContent(requestStream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.unishare.sync+zip");
        using var response = await client.PostAsync("/api/v1/sync", content);
        response.EnsureSuccessStatusCode();
        var responsePackage = Path.Combine(root, responseName);
        await using var output = File.Create(responsePackage);
        await response.Content.CopyToAsync(output);
        return new PackageResponse(responsePackage, response.Headers.GetValues("X-UniShare-Blobs").Single());
    }

    private static async Task<DeltaPackageResponse> PostDeltaAsync(
        HttpClient client,
        string requestPackage,
        string root,
        string responseName,
        long? serverCursor,
        long clientThrough)
    {
        await using var requestStream = File.OpenRead(requestPackage);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/sync")
        {
            Content = new StreamContent(requestStream),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.unishare.sync+zip");
        request.Headers.Add("X-UniShare-Delta", "1");
        request.Headers.Add(
            "X-UniShare-Client-Through", clientThrough.ToString(CultureInfo.InvariantCulture));
        if (serverCursor.HasValue)
        {
            request.Headers.Add(
                "X-UniShare-Server-Cursor", serverCursor.Value.ToString(CultureInfo.InvariantCulture));
        }
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var responsePackage = Path.Combine(root, responseName);
        await using var output = File.Create(responsePackage);
        await response.Content.CopyToAsync(output, TestContext.Current.CancellationToken);
        return new DeltaPackageResponse(
            responsePackage,
            long.Parse(
                response.Headers.GetValues("X-UniShare-Server-Through").Single(),
                CultureInfo.InvariantCulture),
            response.Headers.GetValues("X-UniShare-Client-Ack").Single(),
            response.Headers.GetValues("X-UniShare-Full-Snapshot").Single() == "1");
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<Profile> CreateProfileAsync(string root, string name)
    {
        var profileRoot = Path.Combine(root, name);
        var repository = new SqliteLibraryRepository(Path.Combine(profileRoot, "library.db"));
        var blobStore = new FileBlobStore(profileRoot);
        var library = new LibraryService(repository, blobStore, TimeProvider.System);
        await library.InitializeAsync();
        var backup = new PortableBackupService(profileRoot);
        return new Profile(
            profileRoot,
            library,
            blobStore,
            backup,
            new PortableSyncService(profileRoot, backup, blobStore));
    }

    private sealed record Profile(
        string Root,
        LibraryService Library,
        FileBlobStore BlobStore,
        PortableBackupService Backup,
        PortableSyncService Sync);

    private sealed record PackageResponse(string Path, string BlobHeader);

    private sealed record DeltaPackageResponse(
        string Path,
        long ServerThrough,
        string ClientAck,
        bool FullSnapshot);
}
