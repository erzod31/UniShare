using System.Net;
using System.Text;
using System.Text.Json;
using UniShare.Application;
using UniShare.Infrastructure;

namespace UniShare.Infrastructure.Tests;

public sealed class OriginMetadataContractTests
{
    public static IEnumerable<object[]> Cases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "origin-metadata.json")));
        foreach (var fixture in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            yield return [fixture.GetProperty("name").GetString()!, fixture.GetRawText()];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task OriginalContentMatchesTheSharedPlatformContract(string name, string fixtureJson)
    {
        _ = name;
        using var fixture = JsonDocument.Parse(fixtureJson);
        var value = fixture.RootElement;
        var html = string.Join('\n', value.GetProperty("html").EnumerateArray().Select(line => line.GetString()));
        var url = new Uri(value.GetProperty("url").GetString()!);
        using var temporary = new TemporaryDirectory();
        var profile = Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), new FixtureEndpointPolicy(), profile,
            TimeProvider.System, handler: new FixtureHandler(url, html));

        var metadata = await service.FetchMetadataAsync(url, TestContext.Current.CancellationToken);

        Assert.NotNull(metadata);
        Assert.Equal(value.GetProperty("description").GetString(), metadata.Description);
        if (value.TryGetProperty("author", out var author))
        {
            Assert.Equal(author.GetString(), metadata.Author);
        }
        if (value.TryGetProperty("expectedTitle", out var expectedTitle))
        {
            Assert.Equal(expectedTitle.GetString(), metadata.Title);
        }
        if (value.TryGetProperty("currentTitle", out var currentTitle))
        {
            Assert.Equal(value.GetProperty("resolvedTitle").GetString(), metadata.ResolveTitle(currentTitle.GetString()!, url));
        }
        Assert.Equal("Mi nota personal", metadata.ResolveDescription("Mi nota personal"));
        Assert.Empty(await repository.GetAllAsync(includeDeleted: true, TestContext.Current.CancellationToken));
    }

    private sealed class FixtureHandler(Uri sourceUri, string html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(request.RequestUri == sourceUri ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                RequestMessage = request,
                Content = new StringContent(request.RequestUri == sourceUri ? html : "{}", Encoding.UTF8,
                    request.RequestUri == sourceUri ? "text/html" : "application/json"),
            });
    }

    private sealed class FixtureEndpointPolicy : IRemoteEndpointPolicy
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAndValidateAsync(Uri uri, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Loopback]);
    }
}
