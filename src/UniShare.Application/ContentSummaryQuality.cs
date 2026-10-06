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
            normalized.Contains("ACERCADEPRENSADERECHOSDEAUTORCONTACTARCREADORES", StringComparison.Ordinal);
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
