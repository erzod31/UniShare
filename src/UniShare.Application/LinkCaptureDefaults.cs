namespace UniShare.Application;

public static class LinkCaptureDefaults
{
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
