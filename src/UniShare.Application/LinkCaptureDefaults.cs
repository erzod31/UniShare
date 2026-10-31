using System.Text;
using System.Text.RegularExpressions;

namespace UniShare.Application;

public static partial class LinkCaptureDefaults
{
    public static string? TitleFromCaption(string? caption)
    {
        var firstLine = FirstCaptionLine(caption);
        if (firstLine is null) return null;
        var text = CaptionWhitespaceRegex().Replace(firstLine, " ");
        var tail = CaptionTailRegex().Match(text);
        if (tail.Success && text[..tail.Index].EnumerateRunes().Count() >= 12 &&
            (tail.Value.Trim().Split(' ').Length > 1 || tail.Value.Contains("://", StringComparison.Ordinal)))
        {
            text = text[..tail.Index];
        }
        var characters = text.EnumerateRunes().ToArray();
        if (characters.Length <= 80) return text;
        var prefix = string.Concat(characters.Take(79).Select(character => character.ToString()));
        var boundary = prefix.LastIndexOf(' ');
        if (boundary > 0 && prefix[..boundary].EnumerateRunes().Count() >= 40)
        {
            prefix = prefix[..boundary];
        }
        return prefix.TrimEnd() + "…";
    }

    internal static string? LegacyTitleFromCaption(string? caption)
    {
        var text = FirstCaptionLine(caption);
        if (text is null || text.Length <= 500) return text;
        return text[..(char.IsHighSurrogate(text[499]) ? 499 : 500)];
    }

    private static string? FirstCaptionLine(string? caption)
    {
        var text = caption?.Replace("\r\n", "\n").Replace('\r', '\n').Trim().Split('\n', 2)[0].Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    [GeneratedRegex(@"[\s\p{Z}]+", RegexOptions.CultureInvariant)]
    private static partial Regex CaptionWhitespaceRegex();

    [GeneratedRegex(@"\s+(?:#\S+|https?://\S+)(?:\s+(?:#\S+|https?://\S+))*\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CaptionTailRegex();

    public static string TitleFromUrl(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Usa un enlace http o https válido.", nameof(uri));
        }

        var host = uri.IdnHost;
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            host = host[4..];
        }

        return string.IsNullOrWhiteSpace(host) ? "Enlace guardado" : host;
    }
}
