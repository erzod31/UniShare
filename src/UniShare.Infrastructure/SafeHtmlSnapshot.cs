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

        var post = TikTokMetadata.Extract(html, sourceUri);
        var structuredTitle = ExtractStructuredContent(html, sourceUri, ContentField.Title)?.Text;
        var title = Limit(post?.Title ?? Limit(CleanText(structuredTitle ?? string.Empty), 500) ??
            FirstValue(values, "og:title", "twitter:title") ?? documentTitle, 500);
        var siteName = FirstValue(values, "og:site_name", "application-name");
        var author = post?.Author ?? FirstValue(values, "author", "article:author");
        var promotionalDescription = FirstValue(
            values, "og:description", "twitter:description", "description");
        var contentSummary = post is null ? ExtractContentSummary(html, title, sourceUri) : post.Description;

        return new WebCaptureMetadata(
            title,
            Limit(post?.Source ?? (string.IsNullOrWhiteSpace(siteName) ? sourceUri.Host : siteName), 500),
            Limit(author, 500),
            contentSummary,
            Limit(promotionalDescription, 4000),
            OriginDescriptionFound: post is not null || contentSummary is not null,
            PageTitle: FirstValue(values, "og:title", "twitter:title") ?? documentTitle);
    }

    private static string? ExtractContentSummary(string html, string? title, Uri sourceUri)
    {
        var structuredBody = ExtractStructuredContent(html, sourceUri, ContentField.ArticleBody)?.Text;
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

        var structuredDescription = ExtractStructuredContent(html, sourceUri, ContentField.Description);
        if (structuredDescription is { IsCaption: true })
        {
            return OriginContentRules.NormalizeCaption(structuredDescription.Text);
        }
        var descriptionSummary = BuildSummary(
            structuredDescription is null ? [] : [structuredDescription.Text], title);
        if (descriptionSummary is not null)
        {
            return descriptionSummary;
        }

        if (OriginContentRules.TikTokPostId(sourceUri) is not null)
        {
            return null;
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

    private enum ContentField { ArticleBody, Description, Title }

    private static ContentDescription? ExtractStructuredContent(string html, Uri sourceUri, ContentField field)
    {
        var candidates = new List<ContentDescription>();
        foreach (Match script in JsonLdScriptRegex().Matches(html))
        {
            try
            {
                using var document = JsonDocument.Parse(script.Groups[1].Value);
                FindTypedContent(document.RootElement, sourceUri, field, candidates);
            }
            catch (JsonException)
            {
                // Third-party JSON-LD may be incomplete. It is never interpreted by a text regex.
            }
        }
        var ranked = candidates.OrderByDescending(value => value.HasIdentity).ToArray();
        if (ranked.Length == 0)
        {
            return null;
        }
        var best = ranked.Where(value => value.HasIdentity == ranked[0].HasIdentity).ToArray();
        return best.Select(value => value.Text).Distinct(StringComparer.Ordinal).Count() == 1 ? best[0] : null;
    }

    private static void FindTypedContent(
        JsonElement element, Uri sourceUri, ContentField field, ICollection<ContentDescription> candidates)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var types = ReadContentTypes(element);
            var isCaption = types.Any(CaptionContentTypes.Contains);
            var isArticle = types.Any(ArticleContentTypes.Contains);
            var identity = ReadIdentity(element);
            if ((field == ContentField.ArticleBody ? isArticle && !isCaption : isCaption || isArticle) &&
                OriginContentRules.MatchesContentUrl(identity, sourceUri))
            {
                var keys = field == ContentField.Title ? new[] { "headline", "name" } :
                    field == ContentField.ArticleBody ? ["articleBody"] :
                    isCaption ? ["caption", "description", "text", "articleBody"] : ["description"];
                foreach (var key in keys)
                {
                    if (element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(value.GetString()))
                    {
                        candidates.Add(new ContentDescription(value.GetString()!, field == ContentField.Description && isCaption, identity is not null));
                        break;
                    }
                }
            }
            foreach (var property in element.EnumerateObject())
            {
                FindTypedContent(property.Value, sourceUri, field, candidates);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                FindTypedContent(child, sourceUri, field, candidates);
            }
        }
    }

    private static IEnumerable<string> ReadContentTypes(JsonElement element)
    {
        if (!element.TryGetProperty("@type", out var type))
        {
            return [];
        }
        return (type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().ToArray() : [type])
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString()!
                .Replace("https://schema.org/", string.Empty, StringComparison.Ordinal)
                .Replace("http://schema.org/", string.Empty, StringComparison.Ordinal));
    }

    private static string? ReadIdentity(JsonElement element)
    {
        foreach (var key in new[] { "mainEntityOfPage", "url", "@id" })
        {
            if (!element.TryGetProperty(key, out var identity))
            {
                continue;
            }
            if (identity.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(identity.GetString()))
            {
                return identity.GetString();
            }
            if (identity.ValueKind == JsonValueKind.Object &&
                identity.TryGetProperty("@id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                return id.GetString();
            }
        }
        return null;
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

    private static string? Limit(string? value, int maximumLength) =>
        OriginContentRules.LimitField(value, maximumLength);

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

    [GeneratedRegex("""<script\b[^>]*type\s*=\s*(?:['"]application/ld\+json['"]|application/ld\+json(?=\s|>))[^>]*>(.*?)</script\s*>""", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
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

    private static readonly HashSet<string> ArticleContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Article", "NewsArticle", "BlogPosting", "TechArticle", "Report", "ScholarlyArticle",
    };

    private static readonly HashSet<string> CaptionContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "VideoObject", "AudioObject", "PodcastEpisode", "ImageObject", "Photograph",
        "SocialMediaPosting", "DiscussionForumPosting",
    };

    private sealed record ContentDescription(string Text, bool IsCaption, bool HasIdentity);

    private sealed record ValidatedRenderedImage(string DataUrl, string AlternativeText);
}

internal sealed record RenderedPageImage(string? DataUrl, string? AlternativeText);
