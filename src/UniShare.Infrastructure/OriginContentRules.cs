using System.Text;
using System.Text.RegularExpressions;
using UniShare.Domain;

namespace UniShare.Infrastructure;

internal static partial class OriginContentRules
{
    internal static string? LimitField(string? value, int maximumLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length <= maximumLength)
        {
            return string.IsNullOrEmpty(text) ? null : text;
        }
        var end = char.IsHighSurrogate(text[maximumLength - 1]) ? maximumLength - 1 : maximumLength;
        return text[..end];
    }

    internal static string? NormalizeCaption(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var text = value.ReplaceLineEndings("\n").Trim();
        if (text.Length <= 4000)
        {
            return text;
        }
        var result = new StringBuilder();
        var runes = text.EnumerateRunes().Take(4001).ToArray();
        foreach (var rune in runes.Take(runes.Length > 4000 ? 3999 : runes.Length))
        {
            result.Append(rune);
        }
        return runes.Length > 4000 ? result.Append('…').ToString() : result.ToString();
    }

    internal static string? TikTokPostId(Uri uri)
    {
        var host = uri.IdnHost.TrimEnd('.');
        if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
            !(host.Equals("tiktok.com", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".tiktok.com", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }
        var match = TikTokPathRegex().Match(uri.AbsolutePath);
        return match.Success ? match.Groups["id"].Value : null;
    }

    internal static bool MatchesContentUrl(string? value, Uri sourceUri)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }
        if (!Uri.TryCreate(sourceUri, value, out var target) || target.Scheme is not ("http" or "https"))
        {
            return false;
        }
        return string.Equals(NormalizeUrl(target), NormalizeUrl(sourceUri), StringComparison.Ordinal);
    }

    private static string NormalizeUrl(Uri uri)
    {
        var normalized = new UriBuilder(UrlCanonicalizer.Canonicalize(uri.AbsoluteUri));
        normalized.Path = normalized.Path.TrimEnd('/') is { Length: > 0 } path ? path : "/";
        return normalized.Uri.AbsoluteUri;
    }

    [GeneratedRegex(@"^/(?:@[^/]+/(?:video|photo)|share/video|embed(?:/v2)?|player/v1|v)/(?<id>[0-9]{10,25})(?:\.html)?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex TikTokPathRegex();
}
