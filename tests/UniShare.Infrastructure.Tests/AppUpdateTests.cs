using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UniShare.Application;
using UniShare.Infrastructure;

namespace UniShare.Infrastructure.Tests;

public sealed class AppUpdateTests
{
    private const string Repository = "erzod31/UniShare";
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly byte[] Package = Encoding.UTF8.GetBytes("synthetic package, never executable");

    private static AppUpdateCandidate Candidate() => new(Repository, "0.6.25", "windows-x64",
        "UniShare-Windows-Setup-0.6.25.exe",
        new Uri("https://github.com/erzod31/UniShare/releases/download/v0.6.25/UniShare-Windows-Setup-0.6.25.exe"),
        Package.Length, Convert.ToHexStringLower(SHA256.HashData(Package)));

    private static byte[] Release(string version = "0.6.25", string? url = null, string? digest = null, bool duplicate = false)
    {
        var name = GitHubAppUpdates.FileName("windows-x64", version);
        var asset = new
        {
            name,
            state = "uploaded",
            size = Package.Length,
            browser_download_url = url ?? $"https://github.com/{Repository}/releases/download/v{version}/{name}",
            digest = digest ?? "sha256:" + Candidate().Sha256
        };
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            draft = false,
            prerelease = false,
            tag_name = "v" + version,
            html_url = $"https://github.com/{Repository}/releases/tag/v{version}",
            assets = duplicate ? new[] { asset, asset } : [asset]
        });
    }

    [Theory]
    [InlineData("0.6.24", false)]
    [InlineData("0.6.16", false)]
    [InlineData("0.6.25", true)]
    [InlineData("0.10.0", true)]
    public void OnlyStrictlyNewerStableReleasesAreOffered(string version, bool available)
    {
        var result = GitHubAppUpdates.ParseRelease(Release(version), Repository, "0.6.24", "windows-x64");
        Assert.Equal(available, result.Candidate is not null);
        Assert.Equal(version, result.PublishedVersion);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3-beta")]
    [InlineData("1.2.3.4")]
    [InlineData("999999999999999.1.1")]
    public void AmbiguousVersionsAreRejected(string value) => Assert.Throws<InvalidDataException>(() => AppReleaseVersion.Parse(value));

    [Fact]
    public void WrongOriginMissingDigestDuplicateAndMalformedMetadataAreRejected()
    {
        foreach (var json in new[] { Release(url: "https://example.com/other.exe"), Release(digest: "sha256:bad"),
            Release(duplicate: true), Encoding.UTF8.GetBytes("{}"), Encoding.UTF8.GetBytes("[]") })
        {
            Assert.Throws<InvalidDataException>(() => GitHubAppUpdates.ParseRelease(json, Repository, "0.6.24", "windows-x64"));
        }
    }

    [Fact]
    public async Task VerifiedDownloadRequiresConfirmationAndRechecksBeforeFakeNativeLaunch()
    {
        using var fixture = new Fixture();
        var ready = await fixture.Service.DownloadAsync(Candidate(), cancellationToken: Token);
        Assert.Equal(Package, await File.ReadAllBytesAsync(ready.FilePath, Token));
        Assert.False(await fixture.Service.RequestInstallAsync(ready, false, Token));
        Assert.Equal(0, fixture.Installations);
        Assert.True(await fixture.Service.RequestInstallAsync(ready, true, Token));
        Assert.Equal(1, fixture.Installations);
        Assert.Equal(2, fixture.Verifications);
    }

    [Fact]
    public async Task FileChangedAfterConfirmationIsNeverLaunched()
    {
        using var fixture = new Fixture();
        var ready = await fixture.Service.DownloadAsync(Candidate(), cancellationToken: Token);
        await File.WriteAllBytesAsync(ready.FilePath, new byte[Package.Length], Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.RequestInstallAsync(ready, true, Token));
        Assert.Equal(0, fixture.Installations);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("truncated")]
    [InlineData("oversized")]
    [InlineData("product")]
    [InlineData("signature")]
    public async Task InvalidDownloadsOrPackageIdentityNeverReachInstaller(string failure)
    {
        using var fixture = new Fixture(failure);
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.DownloadAsync(Candidate(), cancellationToken: Token));
        Assert.Equal(0, fixture.Installations);
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.exe", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CancellationCleansPartialAndNeverRequestsInstallation()
    {
        using var fixture = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.DownloadAsync(Candidate(), cancellationToken: cancellation.Token));
        Assert.Equal(0, fixture.Installations);
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.part", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CancellationDuringStreamingRemovesPartial()
    {
        using var fixture = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var progress = new InlineProgress(_ => cancellation.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.DownloadAsync(Candidate(), progress, cancellation.Token));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.part", SearchOption.AllDirectories));
        Assert.Equal(0, fixture.Installations);
    }

    private sealed class InlineProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }

    [Fact]
    public async Task NetworkFailureAndUntrustedRedirectDoNotProduceAnInstaller()
    {
        foreach (var failure in new[] { "network", "redirect" })
        {
            using var fixture = new Fixture(failure);
            await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.DownloadAsync(Candidate(), cancellationToken: Token));
            Assert.Equal(0, fixture.Installations);
        }
    }

    [Fact]
    public async Task OlderChannelIsCurrentAnd404IsNotAnUpdate()
    {
        foreach (var failure in new[] { "older", "404" })
        {
            using var fixture = new Fixture(failure);
            Assert.Null((await fixture.Service.CheckAsync(Token)).Candidate);
        }
    }

    [Fact]
    public async Task WrongPlatformAndDowngradeAreRejectedBeforeNetwork()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.DownloadAsync(Candidate() with { Platform = "android" }, cancellationToken: Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.DownloadAsync(Candidate() with { Version = "0.6.16" }, cancellationToken: Token));
        Assert.Equal(0, fixture.Requests);
    }

    private sealed class Fixture : HttpMessageHandler, IDisposable, IAppUpdatePackageVerifier, IAppUpdateInstaller
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "unishare-update-test-" + Guid.NewGuid().ToString("N"));
        public GitHubAppUpdates Service { get; }
        private readonly HttpClient _http;
        private readonly string? _failure;
        public int Requests { get; private set; }
        public int Verifications { get; private set; }
        public int Installations { get; private set; }

        public Fixture(string? failure = null)
        {
            _failure = failure;
            Directory.CreateDirectory(Root);
            _http = new HttpClient(this, false);
            Service = new(_http, Repository, "0.6.24", Root, this, this);
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            cancellationToken.ThrowIfCancellationRequested();
            if (_failure == "network") { throw new HttpRequestException("Synthetic offline failure"); }
            var response = new HttpResponseMessage(_failure == "404" ? HttpStatusCode.NotFound : HttpStatusCode.OK);
            if (_failure == "redirect")
            {
                response.StatusCode = HttpStatusCode.Redirect;
                response.Headers.Location = new Uri("http://127.0.0.1/other.exe");
            }
            response.Content = new ByteArrayContent(request.RequestUri!.Host == "api.github.com" ? Release("0.6.16") :
                _failure == "hash" ? new byte[Package.Length] : _failure is "size" or "truncated" ? Package[..^1] :
                _failure == "oversized" ? [.. Package, 1] : Package);
            if (_failure is "truncated" or "oversized") { response.Content.Headers.ContentLength = Package.Length; }
            return Task.FromResult(response);
        }
        public Task<string> VerifyAsync(string path, AppUpdateCandidate candidate, CancellationToken cancellationToken)
        {
            Verifications++;
            if (_failure is "product" or "signature") { throw new InvalidDataException("Synthetic identity/signature rejection"); }
            return Task.FromResult("synthetic-verification");
        }
        public Task RequestAsync(VerifiedAppUpdate update, CancellationToken cancellationToken)
        {
            Installations++;
            return Task.CompletedTask;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _http.Dispose();
                var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!Path.GetFullPath(Root).StartsWith(temp, StringComparison.OrdinalIgnoreCase)) { throw new InvalidOperationException(); }
                Directory.Delete(Root, true);
            }
            base.Dispose(disposing);
        }
    }
}
