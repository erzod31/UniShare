using System.Net;
using System.Text;
using System.Text.Json;
using UniShare.Application;
using UniShare.Infrastructure;

namespace UniShare.Infrastructure.Tests;

public sealed class TikTokMetadataFetchTests
{
    private const string Id = "7300000000000000001";
    private static readonly Uri PostUri = new($"https://www.tiktok.com/@creadora/video/{Id}");

    [Fact]
    public async Task ShortLinkUsesTheFinalPostIdentityBeforeReadingHydration()
    {
        using var temporary = new TemporaryDirectory();
        var shortUri = new Uri("https://vm.tiktok.com/ZAexample/");
        var handler = new RecordingHandler(request => request.RequestUri == shortUri
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = PostUri } }
            : Html(CaptionPage("Pie corto 🎯")));
        var policy = new RecordingPolicy();
        using var service = await CreateAsync(temporary.Path, handler, policy);

        var metadata = await service.FetchMetadataAsync(shortUri, TestContext.Current.CancellationToken);

        Assert.Equal("Pie corto 🎯", metadata!.Description);
        Assert.Equal("Autora original", metadata.Author);
        Assert.Equal([shortUri, PostUri], handler.Requests);
        Assert.Equal(handler.Requests, policy.Requests);
    }

    [Theory]
    [InlineData("🫶 #viajes")]
    [InlineData("")]
    public async Task IdentifiedCaptionEvenWhenEmptyDoesNotTriggerAnotherRequest(string caption)
    {
        using var temporary = new TemporaryDirectory();
        var handler = new RecordingHandler(_ => Html(CaptionPage(caption)));
        using var service = await CreateAsync(temporary.Path, handler);

        var metadata = await service.FetchMetadataAsync(PostUri, TestContext.Current.CancellationToken);

        Assert.True(metadata!.OriginDescriptionFound);
        Assert.Equal(caption.Length == 0 ? null : caption, metadata.Description);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task OptionalOEmbedUsesTheSamePostWithoutForwardingQueryOrTracking()
    {
        using var temporary = new TemporaryDirectory();
        var source = new Uri(PostUri.AbsoluteUri + "?utm_source=share&token=private-fixture#fragment");
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath == "/oembed"
            ? Json(EmbedPayload(Id, "🌊 #original")) : Html("<title>Título provisional</title>"));
        var policy = new RecordingPolicy();
        using var service = await CreateAsync(temporary.Path, handler, policy);

        var metadata = await service.FetchMetadataAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal("🌊 #original", metadata!.Description);
        Assert.Equal("Autora API", metadata.Author);
        Assert.Equal(2, handler.Requests.Count);
        var fallback = handler.Requests[1];
        Assert.Equal("www.tiktok.com", fallback.Host);
        Assert.Equal("/oembed", fallback.AbsolutePath);
        Assert.Equal("?url=" + Uri.EscapeDataString(PostUri.AbsoluteUri), fallback.Query);
        Assert.DoesNotContain("private-fixture", fallback.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(handler.Requests, policy.Requests);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    public async Task UnavailableOEmbedKeepsPrimaryMetadataAndDoesNotInventDescription(int status)
    {
        using var temporary = new TemporaryDirectory();
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath == "/oembed"
            ? new HttpResponseMessage((HttpStatusCode)status)
            : Html("<title>Título original</title><meta name='description' content='Compra acceso exclusivo ahora'>"));
        using var service = await CreateAsync(temporary.Path, handler);

        var metadata = await service.FetchMetadataAsync(PostUri, TestContext.Current.CancellationToken);

        Assert.Equal("Título original", metadata!.Title);
        Assert.Null(metadata.Description);
        Assert.False(metadata.OriginDescriptionFound);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData("7300000000000000002", "TikTok")]
    [InlineData(Id, "Another provider")]
    public async Task EmbedWithWrongIdentityOrProviderCannotSupplyACaption(string id, string provider)
    {
        using var temporary = new TemporaryDirectory();
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath == "/oembed"
            ? Json(EmbedPayload(id, "No pertenece al elemento", provider)) : Html("<title>Conservar</title>"));
        using var service = await CreateAsync(temporary.Path, handler);

        var metadata = await service.FetchMetadataAsync(PostUri, TestContext.Current.CancellationToken);

        Assert.Equal("Conservar", metadata!.Title);
        Assert.Null(metadata.Description);
    }

    [Fact]
    public async Task CallerCancellationIsNotSwallowedByTheOptionalFallback()
    {
        using var temporary = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        var handler = new RecordingHandler(_ => Html(CaptionPage("No debe leerse")));
        using var service = await CreateAsync(temporary.Path, handler);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.FetchMetadataAsync(PostUri, cancellation.Token));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CaptionAndTitleLimitsDoNotSplitUnicodeAndMarkTheExcerpt()
    {
        using var temporary = new TemporaryDirectory();
        var caption = string.Concat(Enumerable.Repeat("🌊", 4100));
        var handler = new RecordingHandler(_ => Html(CaptionPage(caption)));
        using var service = await CreateAsync(temporary.Path, handler);

        var metadata = await service.FetchMetadataAsync(PostUri, TestContext.Current.CancellationToken);

        Assert.Equal(string.Concat(Enumerable.Repeat("🌊", 3999)) + "…", metadata!.Description);
        Assert.Equal(string.Concat(Enumerable.Repeat("🌊", 79)) + "…", metadata.Title);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task StructuredTitlesUseTheSameUnicodeBoundaryAsProviderTitles()
    {
        using var temporary = new TemporaryDirectory();
        var title = string.Concat(Enumerable.Repeat("🌊", 501));
        var html = "<script type='application/ld+json'>" + JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["@type"] = "VideoObject",
            ["name"] = title,
            ["description"] = "Pie original",
        }) + "</script>";
        var handler = new RecordingHandler(_ => Html(html));
        using var service = await CreateAsync(temporary.Path, handler);

        var metadata = await service.FetchMetadataAsync(new Uri("https://example.com/video"), TestContext.Current.CancellationToken);

        Assert.Equal(string.Concat(Enumerable.Repeat("🌊", 250)), metadata!.Title);
        Assert.Equal("Pie original", metadata.Description);
    }

    private static string CaptionPage(string caption) =>
        "<script id='SIGI_STATE'>" + JsonSerializer.Serialize(new
        {
            ItemModule = new Dictionary<string, object>
            {
                [Id] = new { id = Id, desc = caption, author = new { nickname = "Autora original" } },
            },
        }) + "</script>";

    private static string EmbedPayload(string id, string caption, string provider = "TikTok") =>
        JsonSerializer.Serialize(new { provider_name = provider, type = "video", embed_product_id = id, title = caption, author_name = "Autora API" });

    private static HttpResponseMessage Html(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "text/html") };
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };

    private static async Task<DurableDownloadService> CreateAsync(string root, HttpMessageHandler handler, RecordingPolicy? policy = null)
    {
        var repository = new SqliteLibraryRepository(Path.Combine(root, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        return new DurableDownloadService(repository, new FileBlobStore(root), policy ?? new RecordingPolicy(), root, TimeProvider.System, handler: handler);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var response = respond(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingPolicy : IRemoteEndpointPolicy
    {
        public List<Uri> Requests { get; } = [];
        public Task<IReadOnlyList<IPAddress>> ResolveAndValidateAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Requests.Add(uri);
            return Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Loopback]);
        }
    }
}
