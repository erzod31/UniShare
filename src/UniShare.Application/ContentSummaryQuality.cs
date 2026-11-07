using System.Text.RegularExpressions;

namespace UniShare.Application;

public static partial class ContentSummaryQuality
{
    public static bool IsKnownNavigationJunk(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = WhitespaceRegex().Replace(value.Trim(), " ").ToUpperInvariant();
        var navigationMatches = NavigationFragments.Count(fragment =>
            normalized.Contains(fragment, StringComparison.Ordinal));
        var whitespace = value.Count(char.IsWhiteSpace);
        var compactNavigation = navigationMatches >= 4 &&
            (whitespace * 20 < value.Length || CamelCaseBoundaryRegex().Count(value) >= 3);
        return compactNavigation ||
            normalized.Contains("OVERPERSAUTEURSRECHTCONTACTCREATORS", StringComparison.Ordinal) ||
            normalized.Contains("ABOUTPRESSCOPYRIGHTCONTACTCREATORS", StringComparison.Ordinal) ||
            normalized.Contains("ACERCADEPRENSADERECHOSDEAUTORCONTACTARCREADORES", StringComparison.Ordinal) ||
            normalized.StartsWith("DISFRUTA DE LOS VÍDEOS Y LA MÚSICA QUE TE GUSTAN", StringComparison.Ordinal) ||
            normalized.StartsWith("DISFRUTA DE LOS VIDEOS Y LA MUSICA QUE TE GUSTAN", StringComparison.Ordinal) ||
            normalized.StartsWith("ENJOY THE VIDEOS AND MUSIC YOU LOVE", StringComparison.Ordinal);
    }

    public static bool IsProvisionalSource(string? source, Uri? sourceUri)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return true;
        }

        var trimmed = source.Trim().TrimEnd('.');
        if (trimmed.Equals("m.youtube.com", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("www.youtube.com", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (sourceUri is null)
        {
            return false;
        }

        var host = sourceUri.IdnHost.TrimEnd('.');
        var bareHost = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] :
            host.StartsWith("m.", StringComparison.OrdinalIgnoreCase) ? host[2..] : host;
        return trimmed.Equals(host, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals(bareHost, StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals($"www.{bareHost}", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals($"m.{bareHost}", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string[] NavigationFragments =
    [
        "AUTEURSRECHT", "CONTACTCREATORS", "ADVERTEREN", "ONTWIKKELAARS", "VOORWAARDEN",
        "PRIVACYBELEID", "ZO WERKT YOUTUBE", "NIEUWE FUNCTIES TESTEN",
        "COPYRIGHT", "CONTACT US", "ADVERTISE", "DEVELOPERS", "TERMS", "PRIVACY POLICY",
        "DERECHOS DE AUTOR", "CONTACTAR", "CREADORES", "PUBLICIDAD", "DESARROLLADORES",
        "TÉRMINOS", "PRIVACIDAD", "CÓMO FUNCIONA YOUTUBE",
    ];

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("(?<=[a-zà-ÿ])(?=[A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex CamelCaseBoundaryRegex();
}
