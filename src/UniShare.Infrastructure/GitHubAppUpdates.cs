using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using UniShare.Application;

namespace UniShare.Infrastructure;

/// <summary>Only stable releases and exact product assets from the configured public GitHub repository.</summary>
public sealed class GitHubAppUpdates(
    HttpClient http, string repository, string currentVersion, string cacheRoot,
    IAppUpdatePackageVerifier verifier, IAppUpdateInstaller installer)
{
    public const long MaximumPackageBytes = 256L * 1024 * 1024;
    private const int MaximumMetadataBytes = 2 * 1024 * 1024;
    private readonly string _cacheRoot = Path.GetFullPath(cacheRoot);

    public static HttpClient CreateHttpClient() => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
    })
    { Timeout = TimeSpan.FromMinutes(10) };

    public static void ValidateRepository(string value)
    {
        if (value is null || value.Length > 160 ||
            !Regex.IsMatch(value, @"^[A-Za-z0-9_-]+/[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant) ||
            value.Split('/').Any(part => part is "." or ".."))
        {
            throw new InvalidDataException("Invalid GitHub repository.");
        }
    }

    public async Task<AppUpdateCheck> CheckAsync(CancellationToken cancellationToken = default)
    {
        ValidateRepository(repository);
        _ = AppReleaseVersion.Parse(currentVersion);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("UniShare-AppUpdates/1");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new(currentVersion, null, null);
        }
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumMetadataBytes)
        {
            throw new InvalidDataException("Release metadata exceeds the limit.");
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        await CopyBoundedAsync(stream, memory, MaximumMetadataBytes, null, cancellationToken);
        return ParseRelease(memory.ToArray(), repository, currentVersion, "windows-x64");
    }

    public static AppUpdateCheck ParseRelease(ReadOnlyMemory<byte> json, string repository, string currentVersion, string platform)
    {
        try { return ParseReleaseCore(json, repository, currentVersion, platform); }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            throw new InvalidDataException("Invalid application release metadata.", error);
        }
    }

    private static AppUpdateCheck ParseReleaseCore(ReadOnlyMemory<byte> json, string repository, string currentVersion, string platform)
    {
        ValidateRepository(repository);
        var current = AppReleaseVersion.Parse(currentVersion);
        if (json.Length > MaximumMetadataBytes) { throw new InvalidDataException("Release metadata exceeds the limit."); }
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var release = document.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
        {
            return new(currentVersion, null, null);
        }
        var tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!tag.StartsWith('v')) { throw new InvalidDataException("Invalid release tag."); }
        var version = AppReleaseVersion.Parse(tag[1..]);
        if (release.GetProperty("html_url").GetString() != $"https://github.com/{repository}/releases/tag/{tag}")
        {
            throw new InvalidDataException("Release belongs to another repository.");
        }
        if (version.CompareTo(current) <= 0) { return new(currentVersion, version.ToString(), null); }
        var name = FileName(platform, version.ToString());
        var assets = release.GetProperty("assets").EnumerateArray().Where(asset => asset.GetProperty("name").GetString() == name).ToArray();
        if (assets.Length != 1) { throw new InvalidDataException("A unique package for this platform is not published."); }
        var asset = assets[0];
        if (asset.GetProperty("state").GetString() != "uploaded") { throw new InvalidDataException("Package upload is incomplete."); }
        var digest = asset.GetProperty("digest").GetString() ?? string.Empty;
        if (!Regex.IsMatch(digest, "^sha256:[a-f0-9]{64}$", RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException("The release has no authenticated SHA-256 digest.");
        }
        var candidate = new AppUpdateCandidate(repository, version.ToString(), platform, name,
            new Uri(asset.GetProperty("browser_download_url").GetString()!, UriKind.Absolute),
            asset.GetProperty("size").GetInt64(), digest[7..]);
        ValidateCandidate(candidate, repository, currentVersion);
        return new(currentVersion, version.ToString(), candidate);
    }

    public static string FileName(string platform, string version) => platform switch
    {
        "windows-x64" => $"UniShare-Windows-Setup-{version}.exe",
        "android" => $"UniShare-Android-{version}.apk",
        _ => throw new InvalidDataException("Unsupported update platform."),
    };

    public static void ValidateCandidate(AppUpdateCandidate candidate, string repository, string currentVersion)
    {
        ValidateRepository(repository);
        if (candidate.Repository != repository ||
            AppReleaseVersion.Parse(candidate.Version).CompareTo(AppReleaseVersion.Parse(currentVersion)) <= 0 ||
            candidate.FileName != FileName(candidate.Platform, candidate.Version) ||
            candidate.DownloadUrl.AbsoluteUri != $"https://github.com/{repository}/releases/download/v{candidate.Version}/{candidate.FileName}" ||
            candidate.Bytes is <= 0 or > MaximumPackageBytes ||
            !Regex.IsMatch(candidate.Sha256, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException("Invalid update identity, version, origin, size or hash.");
        }
    }

    public async Task<VerifiedAppUpdate> DownloadAsync(AppUpdateCandidate candidate, IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateForWindows(candidate);
        var directory = Path.Combine(_cacheRoot, candidate.Version + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, candidate.FileName);
        var partial = path + ".part";
        try
        {
            using var response = await OpenDownloadAsync(candidate.DownloadUrl, cancellationToken);
            if (response.Content.Headers.ContentLength is { } length && length != candidate.Bytes)
            {
                throw new InvalidDataException("Package length differs from the release.");
            }
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var received = await CopyBoundedAsync(input, output, candidate.Bytes, progress, cancellationToken);
                if (received != candidate.Bytes) { throw new InvalidDataException("Package download is truncated."); }
            }
            await VerifyHashAsync(partial, candidate, cancellationToken);
            var signature = await verifier.VerifyAsync(partial, candidate, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partial, path);
            return new(candidate, path, signature);
        }
        catch
        {
            File.Delete(partial);
            throw;
        }
    }

    public async Task<bool> RequestInstallAsync(VerifiedAppUpdate update, bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!userConfirmed) { return false; }
        ValidateForWindows(update.Candidate);
        var path = Path.GetFullPath(update.FilePath);
        if (!path.StartsWith(_cacheRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(path) != update.Candidate.FileName)
        {
            throw new InvalidDataException("Package is outside the update cache.");
        }
        await VerifyHashAsync(path, update.Candidate, cancellationToken);
        _ = await verifier.VerifyAsync(path, update.Candidate, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await installer.RequestAsync(update, cancellationToken);
        return true;
    }

    private void ValidateForWindows(AppUpdateCandidate candidate)
    {
        ValidateCandidate(candidate, repository, currentVersion);
        if (candidate.Platform != "windows-x64") { throw new InvalidDataException("This is not a Windows installer."); }
    }

    private static async Task VerifyHashAsync(string path, AppUpdateCandidate candidate, CancellationToken token)
    {
        if (new FileInfo(path).Length != candidate.Bytes) { throw new InvalidDataException("Package size changed."); }
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), Convert.FromHexString(candidate.Sha256)))
        {
            throw new InvalidDataException("Package SHA-256 does not match the release.");
        }
    }

    private async Task<HttpResponseMessage> OpenDownloadAsync(Uri location, CancellationToken token)
    {
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            if (location.Scheme != "https" || !string.IsNullOrEmpty(location.UserInfo) ||
                !string.IsNullOrEmpty(location.Fragment) || location.Port != 443 ||
                location.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
            {
                throw new InvalidDataException("Untrusted package redirect.");
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, location);
            request.Headers.UserAgent.ParseAdd("UniShare-AppUpdates/1");
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var target = response.Headers.Location;
                response.Dispose();
                if (target is null) { throw new InvalidDataException("Invalid package redirect."); }
                location = target.IsAbsoluteUri ? target : new Uri(location, target);
                continue;
            }
            try { response.EnsureSuccessStatusCode(); return response; }
            catch { response.Dispose(); throw; }
        }
        throw new InvalidDataException("Too many package redirects.");
    }

    private static async Task<long> CopyBoundedAsync(Stream input, Stream output, long maximum,
        IProgress<long>? progress, CancellationToken token)
    {
        var buffer = new byte[65536];
        long received = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            received = checked(received + count);
            if (received > maximum) { throw new InvalidDataException("Response exceeds its permitted size."); }
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            progress?.Report(received);
        }
        return received;
    }
}
