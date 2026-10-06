using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UniShare.Application;

namespace UniShare.Infrastructure;

internal static partial class SafeHtmlSnapshot
{
    private const long MaximumInputBytes = 16L * 1024 * 1024;
    private const int MaximumRenderedTextLength = 1_000_000;
    private const int MaximumRenderedImages = 12;
    private const int MaximumRenderedImageBytes = 1_500_000;
    private const int MaximumRenderedImagesBytes = 8_000_000;
    private const int MaximumSummaryLength = 600;
    private const int MinimumUsefulTextLength = 60;

    public static async Task<WebCaptureMetadata> RewriteAsync(
        string path,
        Uri sourceUri,
        CancellationToken cancellationToken = default)
    {
        if (new FileInfo(path).Length > MaximumInputBytes)
        {
            throw new InvalidDataException("La página HTML supera el límite de captura de 16 MiB.");
        }

        var source = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
        var metadata = ExtractMetadata(source, sourceUri);
        var title = metadata.Title ?? sourceUri.Host;
        var withoutActiveContent = ActiveContentRegex().Replace(source, string.Empty);
        var withLineBreaks = BlockTagRegex().Replace(withoutActiveContent, Environment.NewLine);
        var text = CleanText(AllTagsRegex().Replace(withLineBreaks, string.Empty));
        var document = BuildDocument(title, sourceUri, text);
        var safePath = path + ".safe";
        try
        {
            await using (var stream = new FileStream(
                safePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteAsync(document.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            File.Move(safePath, path, overwrite: true);
            return metadata;
        }
        finally
        {
            if (File.Exists(safePath))
            {
                File.Delete(safePath);
            }
        }
    }

    public static async Task WriteRenderedAsync(
        string path,
        Uri sourceUri,
        string title,
        string renderedText,
        IReadOnlyCollection<RenderedPageImage>? images,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(renderedText);
        if (renderedText.Length > MaximumRenderedTextLength)
        {
            throw new InvalidDataException("El texto renderizado supera el límite de 1 000 000 de caracteres.");
        }

        var validatedImages = ValidateRenderedImages(images ?? []);
        var document = BuildRenderedDocument(
            Limit(CleanText(title), 500) ?? sourceUri.Host,
            sourceUri,
            CleanText(renderedText),
            validatedImages);
        await WriteAtomicallyAsync(path, document, cancellationToken);
    }

    internal static WebCaptureMetadata ExtractMetadata(string html, Uri sourceUri)
    {
        var titleMatch = TitleRegex().Match(html);
        var documentTitle = titleMatch.Success ? Limit(CleanText(titleMatch.Groups[1].Value), 500) : null;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tag in MetaTagRegex().Matches(html))
        {
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attribute in AttributeRegex().Matches(tag.Value))
            {
                var value = attribute.Groups["dq"].Success ? attribute.Groups["dq"].Value :
                    attribute.Groups["sq"].Success ? attribute.Groups["sq"].Value :
                    attribute.Groups["bare"].Value;
                attributes[attribute.Groups["name"].Value] = value;
            }

            if (attributes.TryGetValue("content", out var content) &&
                (attributes.TryGetValue("name", out var key) || attributes.TryGetValue("property", out key)))
            {
                values.TryAdd(key, CleanText(content));
            }
        }

        var title = FirstValue(values, "og:title", "twitter:title") ?? documentTitle;
        var siteName = FirstValue(values, "og:site_name", "application-name");
        var author = FirstValue(values, "author", "article:author");
        var promotionalDescription = FirstValue(
            values, "og:description", "twitter:description", "description");
        var contentSummary = ExtractContentSummary(html, title, sourceUri);

        return new WebCaptureMetadata(
            title,
            Limit(string.IsNullOrWhiteSpace(siteName) ? sourceUri.Host : siteName, 500),
            Limit(author, 500),
            contentSummary,
            Limit(promotionalDescription, 4000));
    }

    private static string? ExtractContentSummary(string html, string? title, Uri sourceUri)
    {
        var structuredBody = ExtractStructuredArticleBody(html);
        var structuredSummary = BuildSummary(
            string.IsNullOrWhiteSpace(structuredBody) ? [] : [structuredBody], title);
        if (structuredSummary is not null)
        {
            return structuredSummary;
        }

        if (IsYouTubeVideoUri(sourceUri))
        {
            var videoDescription = ExtractYouTubeVideoDescription(html);
            var videoSummary = BuildSummary(
                string.IsNullOrWhiteSpace(videoDescription) ? [] : [videoDescription],
                title);
            if (videoSummary is not null)
            {
                return videoSummary;
            }
        }

        var structuredDescription = ExtractStructuredDescription(html);
        var descriptionSummary = BuildSummary(
            string.IsNullOrWhiteSpace(structuredDescription) ? [] : [structuredDescription], title);
        if (descriptionSummary is not null)
        {
            return descriptionSummary;
        }

        var cleaned = ActiveContentRegex().Replace(html, string.Empty);
        cleaned = NonContentRegionRegex().Replace(cleaned, string.Empty);
        var semanticScope = SelectContentScope(cleaned);
        var scope = semanticScope ?? FirstInnerHtml(BodyRegionRegex(), cleaned) ?? cleaned;
        var paragraphs = ExtractParagraphs(scope);
        if (paragraphs.Count == 0 && semanticScope is not null)
        {
            var withLines = BlockTagRegex().Replace(scope, Environment.NewLine);
            paragraphs.AddRange(CleanText(AllTagsRegex().Replace(withLines, string.Empty))
                .Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        }
        return BuildSummary(paragraphs, title);
    }

    private static string? SelectContentScope(string html)
    {
        return ArticleRegionRegex().Matches(html)
            .Concat(MainRegionRegex().Matches(html))
            .Select(match => match.Groups[1].Value)
            .Select(value => new
            {
                Value = value,
                Score = ExtractParagraphs(value).Sum(paragraph => paragraph.Length),
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Value.Length)
            .Select(candidate => candidate.Value)
            .FirstOrDefault();
    }

    private static string? ExtractStructuredArticleBody(string html)
    {
        foreach (Match script in JsonLdScriptRegex().Matches(html))
        {
            try
            {
                using var document = JsonDocument.Parse(script.Groups[1].Value);
                var body = FindArticleBody(document.RootElement);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    return CleanText(body);
                }
            }
            catch (JsonException)
            {
                // El HTML de terceros puede incluir JSON-LD inválido; se continúa con el cuerpo visible.
            }
        }
        return null;
    }

    private static string? ExtractStructuredDescription(string html)
    {
        foreach (Match script in JsonLdScriptRegex().Matches(html))
        {
            try
            {
                using var document = JsonDocument.Parse(script.Groups[1].Value);
                var description = FindTypedDescription(document.RootElement);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    return CleanText(description);
                }
            }
            catch (JsonException)
            {
                // El JSON-LD de terceros puede estar incompleto; se continúa con el HTML visible.
            }
        }
        return null;
    }

    private static string? FindTypedDescription(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (IsSupportedContentType(element) &&
                element.TryGetProperty("description", out var description) &&
                description.ValueKind == JsonValueKind.String)
            {
                return description.GetString();
            }
            foreach (var property in element.EnumerateObject())
            {
                var nested = FindTypedDescription(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var nested = FindTypedDescription(child);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        return null;
    }

    private static bool IsSupportedContentType(JsonElement element)
    {
        if (!element.TryGetProperty("@type", out var type))
        {
            return false;
        }
        return type.ValueKind switch
        {
            JsonValueKind.String => SupportedContentTypes.Contains(type.GetString() ?? string.Empty),
            JsonValueKind.Array => type.EnumerateArray().Any(value =>
                value.ValueKind == JsonValueKind.String && SupportedContentTypes.Contains(value.GetString() ?? string.Empty)),
            _ => false,
        };
    }

    private static string? ExtractYouTubeVideoDescription(string html)
    {
        var match = YouTubeShortDescriptionRegex().Match(html);
        if (!match.Success)
        {
            return null;
        }
        try
        {
            return CleanText(JsonSerializer.Deserialize<string>($"\"{match.Groups["value"].Value}\"") ?? string.Empty);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsYouTubeVideoUri(Uri uri)
    {
        var host = uri.IdnHost.TrimEnd('.');
        var youtubeHost = host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);
        return youtubeHost && (host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.StartsWith("/shorts/", StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.StartsWith("/live/", StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindArticleBody(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("articleBody") && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }
            foreach (var property in element.EnumerateObject())
            {
                var nested = FindArticleBody(property.Value);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var nested = FindArticleBody(child);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        return null;
    }

    private static List<string> ExtractParagraphs(string scope)
    {
        var paragraphs = new List<string>();
        foreach (Match match in ParagraphRegex().Matches(scope))
        {
            var raw = match.Groups[1].Value;
            var text = CleanText(AllTagsRegex().Replace(raw, " "));
            if (text.Length < MinimumUsefulTextLength)
            {
                continue;
            }

            var linkedCharacters = AnchorRegex().Matches(raw)
                .Select(link => CleanText(AllTagsRegex().Replace(link.Groups[1].Value, " ")).Length)
                .Sum();
            if (linkedCharacters * 2 > text.Length || LooksLikeBoilerplate(text))
            {
                continue;
            }
            paragraphs.Add(text);
        }
        return paragraphs;
    }

    private static string? BuildSummary(IEnumerable<string> sourceParagraphs, string? title)
    {
        var normalizedTitle = NormalizeForComparison(title);
        var sentences = new List<string>();
        foreach (var paragraph in sourceParagraphs)
        {
            foreach (var line in CleanText(paragraph).Split(
                ['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var cleaned = line;
                if (!string.IsNullOrWhiteSpace(title) && cleaned.StartsWith(title, StringComparison.OrdinalIgnoreCase))
                {
                    cleaned = cleaned[title.Length..].TrimStart(' ', '-', '–', '—', ':', '|');
                }
                if (cleaned.Length < 35 || LooksLikeBoilerplate(cleaned) || !LooksLikeProse(cleaned) ||
                    NormalizeForComparison(cleaned) == normalizedTitle ||
                    cleaned.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    cleaned.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var sentence in SentenceBoundaryRegex().Split(cleaned))
                {
                    var candidate = sentence.Trim();
                    if (candidate.Length >= 35 && LooksLikeProse(candidate) &&
                        NormalizeForComparison(candidate) != normalizedTitle)
                    {
                        sentences.Add(candidate);
                    }
                }
            }
        }

        if (sentences.Count == 0)
        {
            return null;
        }

        var summary = new StringBuilder();
        foreach (var sentence in sentences.Take(4))
        {
            var separatorLength = summary.Length == 0 ? 0 : 1;
            if (summary.Length + separatorLength + sentence.Length > MaximumSummaryLength)
            {
                if (summary.Length == 0)
                {
                    return ClipAtWord(sentence, MaximumSummaryLength);
                }
                break;
            }
            if (summary.Length > 0)
            {
                summary.Append(' ');
            }
            summary.Append(sentence);
            if (summary.Length >= 260)
            {
                break;
            }
        }
        return summary.Length == 0 ? null : summary.ToString();
    }

    private static string ClipAtWord(string value, int maximumLength)
    {
        if (value.Length <= maximumLength)
        {
            return value;
        }
        var boundary = value.LastIndexOf(' ', maximumLength - 1);
        return $"{value[..(boundary > 80 ? boundary : maximumLength - 1)].TrimEnd()}…";
    }

    private static string? FirstInnerHtml(Regex regex, string value)
    {
        var match = regex.Match(value);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string NormalizeForComparison(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : InlineWhitespaceRegex().Replace(value.Trim(), " ").ToUpperInvariant();

    private static bool LooksLikeBoilerplate(string value)
    {
        var normalized = NormalizeForComparison(value);
        return normalized.Contains("POLÍTICA DE COOKIES", StringComparison.Ordinal) ||
            normalized.Contains("COOKIE POLICY", StringComparison.Ordinal) ||
            normalized.Contains("TODOS LOS DERECHOS RESERVADOS", StringComparison.Ordinal) ||
            normalized.Contains("ALL RIGHTS RESERVED", StringComparison.Ordinal) ||
            normalized.StartsWith("ACEPTAR COOKIES", StringComparison.Ordinal) ||
            normalized.StartsWith("INICIAR SESIÓN", StringComparison.Ordinal) ||
            normalized.StartsWith("SIGN IN", StringComparison.Ordinal) ||
            ContentSummaryQuality.IsKnownNavigationJunk(value);
    }

    private static bool LooksLikeProse(string value)
    {
        var letters = value.Count(char.IsLetter);
        if (letters < 25)
        {
            return false;
        }
        var latinLetters = value.Count(character =>
            (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z') ||
            (character >= '\u00C0' && character <= '\u024F'));
        if (latinLetters * 4 < letters * 3)
        {
            return true;
        }
        var whitespace = value.Count(char.IsWhiteSpace);
        var camelTransitions = CamelCaseBoundaryRegex().Count(value);
        return whitespace >= 5 && whitespace * 18 >= value.Length && camelTransitions <= 3;
    }

    private static string? FirstValue(Dictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }
        return null;
    }

    private static string? Limit(string? value, int maximumLength) => string.IsNullOrWhiteSpace(value)
        ? null
        : value.Length <= maximumLength ? value : value[..maximumLength];

    private static string CleanText(string value)
    {
        var decoded = WebUtility.HtmlDecode(value).Replace("\0", string.Empty, StringComparison.Ordinal);
        var lines = decoded.Split(['\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return string.Join(Environment.NewLine, lines.Select(line => InlineWhitespaceRegex().Replace(line, " ")));
    }

    private static string BuildDocument(string title, Uri sourceUri, string text) => $$"""
        <!doctype html>
        <html lang="es">
        <head>
          <meta charset="utf-8">
          <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'none'; media-src 'none'; frame-src 'none'; connect-src 'none'; script-src 'none'; style-src 'unsafe-inline'">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>{{WebUtility.HtmlEncode(title)}}</title>
          <style>body{max-width:76ch;margin:3rem auto;padding:0 1.25rem;font:18px/1.65 system-ui,sans-serif;color:#20242b;background:#fff}header{border-bottom:1px solid #ddd;margin-bottom:2rem}pre{white-space:pre-wrap;font:inherit}.source{color:#59636f;overflow-wrap:anywhere}</style>
        </head>
        <body>
          <header><h1>{{WebUtility.HtmlEncode(title)}}</h1><p class="source">Origen: {{WebUtility.HtmlEncode(sourceUri.AbsoluteUri)}}</p></header>
          <main><pre>{{WebUtility.HtmlEncode(text)}}</pre></main>
        </body>
        </html>
        """;

    private static string BuildRenderedDocument(
        string title,
        Uri sourceUri,
        string text,
        IReadOnlyCollection<ValidatedRenderedImage> images)
    {
        var figures = new StringBuilder();
        foreach (var image in images)
        {
            figures.Append("<figure><img loading=\"lazy\" src=\"")
                .Append(image.DataUrl)
                .Append("\" alt=\"")
                .Append(WebUtility.HtmlEncode(image.AlternativeText))
                .AppendLine("\"></figure>");
        }

        return $$"""
            <!doctype html>
            <html lang="es">
            <head>
              <meta charset="utf-8">
              <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; media-src 'none'; frame-src 'none'; connect-src 'none'; script-src 'none'; style-src 'unsafe-inline'">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{WebUtility.HtmlEncode(title)}}</title>
              <style>body{max-width:76ch;margin:3rem auto;padding:0 1.25rem;font:18px/1.65 system-ui,sans-serif;color:#20242b;background:#fff}header{border-bottom:1px solid #ddd;margin-bottom:2rem}pre{white-space:pre-wrap;font:inherit}.source{color:#59636f;overflow-wrap:anywhere}figure{margin:1.5rem 0}img{display:block;max-width:100%;height:auto;margin:auto;border-radius:.5rem}</style>
            </head>
            <body>
              <header><h1>{{WebUtility.HtmlEncode(title)}}</h1><p class="source">Origen: {{WebUtility.HtmlEncode(sourceUri.AbsoluteUri)}}</p></header>
              <main><pre>{{WebUtility.HtmlEncode(text)}}</pre>{{figures}}</main>
            </body>
            </html>
            """;
    }

    private static List<ValidatedRenderedImage> ValidateRenderedImages(
        IReadOnlyCollection<RenderedPageImage> images)
    {
        if (images.Count > MaximumRenderedImages)
        {
            throw new InvalidDataException($"La captura incluye más de {MaximumRenderedImages} imágenes.");
        }

        var result = new List<ValidatedRenderedImage>(images.Count);
        var totalBytes = 0;
        foreach (var image in images)
        {
            var match = ImageDataUrlRegex().Match(image.DataUrl ?? string.Empty);
            if (!match.Success)
            {
                throw new InvalidDataException("La captura contiene una imagen con formato no permitido.");
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(match.Groups["data"].Value);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException("La captura contiene una imagen dañada.", exception);
            }
            if (bytes.Length is 0 or > MaximumRenderedImageBytes)
            {
                throw new InvalidDataException("Una imagen renderizada supera el límite de 1,5 MB.");
            }

            totalBytes += bytes.Length;
            if (totalBytes > MaximumRenderedImagesBytes)
            {
                throw new InvalidDataException("Las imágenes renderizadas superan el límite total de 8 MB.");
            }

            var mimeType = match.Groups["mime"].Value.ToLowerInvariant();
            result.Add(new ValidatedRenderedImage(
                $"data:image/{mimeType};base64,{Convert.ToBase64String(bytes)}",
                Limit(CleanText(image.AlternativeText ?? string.Empty), 500) ?? string.Empty));
        }

        return result;
    }

    private static async Task WriteAtomicallyAsync(
        string path,
        string document,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + $".{Guid.NewGuid():N}.partial";
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteAsync(document.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    [GeneratedRegex("^data:image/(?<mime>png|jpeg|webp|gif);base64,(?<data>[A-Za-z0-9+/=]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ImageDataUrlRegex();

    [GeneratedRegex(@"<(script|style|noscript|iframe|object|embed)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ActiveContentRegex();

    [GeneratedRegex(@"<(?<region>nav|aside|footer|header|form|dialog|menu|svg|canvas)\b[^>]*>.*?</\k<region>\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex NonContentRegionRegex();

    [GeneratedRegex("""<script\b[^>]*type\s*=\s*['"]application/ld\+json['"][^>]*>(.*?)</script\s*>""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex JsonLdScriptRegex();

    [GeneratedRegex(@"<article\b[^>]*>(.*?)</article\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ArticleRegionRegex();

    [GeneratedRegex(@"<main\b[^>]*>(.*?)</main\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex MainRegionRegex();

    [GeneratedRegex(@"<body\b[^>]*>(.*?)</body\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex BodyRegionRegex();

    [GeneratedRegex(@"<p\b[^>]*>(.*?)</p\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ParagraphRegex();

    [GeneratedRegex(@"<a\b[^>]*>(.*?)</a\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex(@"(?<=[.!?…])\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBoundaryRegex();

    [GeneratedRegex(@"<title\b[^>]*>(.*?)</title\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"<meta\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex MetaTagRegex();

    [GeneratedRegex("""(?<name>[\w:-]+)\s*=\s*(?:"(?<dq>[^"]*)"|'(?<sq>[^']*)'|(?<bare>[^\s>]+))""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AttributeRegex();

    [GeneratedRegex(@"</?(?:p|div|br|li|h[1-6]|tr|section|article|header|footer)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex AllTagsRegex();

    [GeneratedRegex(@"[\t\f\v ]+", RegexOptions.CultureInvariant)]
    private static partial Regex InlineWhitespaceRegex();

    [GeneratedRegex("(?<=[a-zà-ÿ])(?=[A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex CamelCaseBoundaryRegex();

    [GeneratedRegex("\\\"shortDescription\\\"\\s*:\\s*\\\"(?<value>(?:\\\\.|[^\\\"\\\\])*)\\\"", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex YouTubeShortDescriptionRegex();

    private static readonly HashSet<string> SupportedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Article", "NewsArticle", "BlogPosting", "TechArticle", "Report", "ScholarlyArticle",
        "VideoObject", "AudioObject", "PodcastEpisode",
    };

    private sealed record ValidatedRenderedImage(string DataUrl, string AlternativeText);
}

internal sealed record RenderedPageImage(string? DataUrl, string? AlternativeText);
