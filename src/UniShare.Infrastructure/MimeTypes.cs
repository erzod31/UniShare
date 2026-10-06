namespace UniShare.Infrastructure;

internal static class MimeTypes
{
    private static readonly Dictionary<string, string> Values =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".txt"] = "text/plain",
            [".md"] = "text/markdown",
            [".html"] = "text/html",
            [".htm"] = "text/html",
            [".json"] = "application/json",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".svg"] = "image/svg+xml",
            [".mp3"] = "audio/mpeg",
            [".mp4"] = "video/mp4",
            [".zip"] = "application/zip",
        };

    public static string FromFileName(string fileName) =>
        Values.TryGetValue(Path.GetExtension(fileName), out var value)
            ? value
            : "application/octet-stream";
}
