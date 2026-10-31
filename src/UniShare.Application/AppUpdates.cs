using System.Globalization;
using System.Text.RegularExpressions;

namespace UniShare.Application;

public readonly record struct AppReleaseVersion(int Major, int Minor, int Patch) : IComparable<AppReleaseVersion>
{
    public static AppReleaseVersion Parse(string value)
    {
        if (value is null || value.Length > 32 ||
            !Regex.IsMatch(value, @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException("Invalid release version.");
        }
        var parts = value.Split('.');
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            throw new InvalidDataException("Invalid release version.");
        }
        return new(major, minor, patch);
    }

    public int CompareTo(AppReleaseVersion other)
    {
        var major = Major.CompareTo(other.Major);
        var minor = Minor.CompareTo(other.Minor);
        return major != 0 ? major : minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}");
    public static bool operator <(AppReleaseVersion left, AppReleaseVersion right) => left.CompareTo(right) < 0;
    public static bool operator <=(AppReleaseVersion left, AppReleaseVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >(AppReleaseVersion left, AppReleaseVersion right) => left.CompareTo(right) > 0;
    public static bool operator >=(AppReleaseVersion left, AppReleaseVersion right) => left.CompareTo(right) >= 0;
}

public sealed record AppUpdateCandidate(
    string Repository, string Version, string Platform, string FileName, Uri DownloadUrl, long Bytes, string Sha256);

public sealed record AppUpdateCheck(string CurrentVersion, string? PublishedVersion, AppUpdateCandidate? Candidate);
public sealed record VerifiedAppUpdate(AppUpdateCandidate Candidate, string FilePath, string SignatureStatus);

public interface IAppUpdatePackageVerifier
{
    Task<string> VerifyAsync(string path, AppUpdateCandidate candidate, CancellationToken cancellationToken);
}

public interface IAppUpdateInstaller
{
    Task RequestAsync(VerifiedAppUpdate update, CancellationToken cancellationToken);
}
