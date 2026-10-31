using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UniShare.Infrastructure;

namespace UniShare.Infrastructure.Tests;

public sealed class TranslationAssetCacheTests
{
    [Fact]
    public async Task VerifiedModelsSurviveRestartAndNoNetworkIsNeededForACachedModel()
    {
        using var directory = new TemporaryDirectory();
        var bytes = Encoding.UTF8.GetBytes("a synthetic immutable model 🌍");
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        using (var online = new TranslationAssetCache(Catalog(bytes), directory.Path, new Handler(bytes)))
        {
            Assert.Equal(bytes, await File.ReadAllBytesAsync(await online.GetAsync(hash, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));
        }
        using var offline = new TranslationAssetCache(Catalog(bytes), directory.Path, new Handler(null));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(await offline.GetAsync(hash, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedTransfersNeverPromoteAnIncompleteOrCorruptModel(bool wrongHash)
    {
        using var directory = new TemporaryDirectory();
        var bytes = Encoding.UTF8.GetBytes("valid model");
        var invalid = wrongHash ? Encoding.UTF8.GetBytes("wrong model") : bytes[..^1];
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        using var cache = new TranslationAssetCache(Catalog(bytes), directory.Path, new Handler(invalid));
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.GetAsync(hash, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task ACorruptCacheIsRepairedOnlyByAVerifiedCopy()
    {
        using var directory = new TemporaryDirectory();
        var bytes = Encoding.UTF8.GetBytes("valid model");
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, hash), Encoding.UTF8.GetBytes("wrong model"), TestContext.Current.CancellationToken);
        using var cache = new TranslationAssetCache(Catalog(bytes), directory.Path, new Handler(bytes));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(await cache.GetAsync(hash, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("https://example.com/model")]
    [InlineData("http://firefox-settings-attachments.cdn.mozilla.net/model")]
    [InlineData("https://key@firefox-settings-attachments.cdn.mozilla.net/model")]
    public void TheModelCatalogCannotChooseArbitraryHostsOrCredentials(string url)
    {
        using var directory = new TemporaryDirectory();
        Assert.Throws<InvalidDataException>(() => new TranslationAssetCache(Catalog([1, 2, 3], url), directory.Path));
    }

    [Fact]
    public async Task ArbitraryHashesAreRejectedBeforeNetworking()
    {
        using var directory = new TemporaryDirectory();
        using var cache = new TranslationAssetCache(Catalog([1, 2, 3]), directory.Path, new Handler(null));
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.GetAsync("../../other", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.GetAsync(new string('a', 64), TestContext.Current.CancellationToken));
    }

    private static string Catalog(byte[] bytes, string url = "https://firefox-settings-attachments.cdn.mozilla.net/public-model.zst") =>
        JsonSerializer.Serialize(new
        {
            pairs = new Dictionary<string, object>
            {
                ["en-es"] = new
                {
                    files = new
                    {
                        model = new
                        {
                            sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                            size = bytes.Length,
                            url,
                            decompressedSha256 = new string('b', 64),
                            decompressedSize = 1024
                        }
                    }
                },
            },
        });

    private sealed class Handler(byte[]? bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (bytes is null)
            {
                throw new HttpRequestException("Network intentionally unavailable");
            }
            Assert.Equal("GET", request.Method.Method);
            Assert.Null(request.Content);
            Assert.Empty(request.RequestUri!.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
