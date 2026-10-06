namespace UniShare.Domain;

public static class UrlCanonicalizer
{
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid",
        "gclid",
        "mc_cid",
        "mc_eid",
    };

    public static Uri ParseHttpUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("La URL debe ser absoluta y usar http o https.", nameof(value));
        }

        return uri;
    }

    public static string Canonicalize(string original)
    {
        var uri = ParseHttpUrl(original);
        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
            Host = uri.IdnHost.ToLowerInvariant(),
        };

        if ((builder.Scheme == Uri.UriSchemeHttp && builder.Port == 80) ||
            (builder.Scheme == Uri.UriSchemeHttps && builder.Port == 443))
        {
            builder.Port = -1;
        }

        if (uri.Query.Length > 1)
        {
            var retained = uri.Query[1..]
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !IsTrackingParameter(part));
            builder.Query = string.Join('&', retained);
        }

        return builder.Uri.AbsoluteUri;
    }

    private static bool IsTrackingParameter(string pair)
    {
        var separator = pair.IndexOf('=');
        var encodedName = separator >= 0 ? pair[..separator] : pair;
        var name = Uri.UnescapeDataString(encodedName.Replace('+', ' '));
        return name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) ||
            TrackingParameters.Contains(name);
    }
}

