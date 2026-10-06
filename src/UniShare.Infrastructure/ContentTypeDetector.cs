using System.Text;

namespace UniShare.Infrastructure;

internal static class ContentTypeDetector
{
    private const int MaximumHeaderBytes = 512;
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
    private static ReadOnlySpan<byte> JpegSignature => [0xff, 0xd8, 0xff];
    private static ReadOnlySpan<byte> ZipFileSignature => [0x50, 0x4b, 0x03, 0x04];
    private static ReadOnlySpan<byte> ZipEmptySignature => [0x50, 0x4b, 0x05, 0x06];
    private static ReadOnlySpan<byte> ZipSpannedSignature => [0x50, 0x4b, 0x07, 0x08];

    public static async Task<string?> DetectFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[MaximumHeaderBytes];
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, MaximumHeaderBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var count = await stream.ReadAsync(buffer, cancellationToken);
        return Detect(buffer.AsSpan(0, count));
    }

    public static string? Detect(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith("%PDF-"u8))
        {
            return "application/pdf";
        }

        if (content.StartsWith(PngSignature))
        {
            return "image/png";
        }

        if (content.StartsWith(JpegSignature))
        {
            return "image/jpeg";
        }

        if (content.StartsWith("GIF87a"u8) || content.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) &&
            content.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        if (content.StartsWith(ZipFileSignature) || content.StartsWith(ZipEmptySignature) ||
            content.StartsWith(ZipSpannedSignature))
        {
            return "application/zip";
        }

        if (content.StartsWith("ID3"u8) ||
            (content.Length >= 2 && content[0] == 0xff && (content[1] & 0xe0) == 0xe0))
        {
            return "audio/mpeg";
        }

        if (content.Length >= 12 && content.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return "video/mp4";
        }

        return DetectTextType(content);
    }

    private static string? DetectTextType(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty || content.IndexOf((byte)0) >= 0)
        {
            return null;
        }

        var text = Encoding.UTF8.GetString(content).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (text.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("<html", StringComparison.OrdinalIgnoreCase))
        {
            return "text/html";
        }

        if (text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("<svg", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase))
        {
            return "image/svg+xml";
        }

        return null;
    }
}
