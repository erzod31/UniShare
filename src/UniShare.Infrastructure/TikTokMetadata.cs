using System.Text.Json;
using System.Text.RegularExpressions;
using UniShare.Application;

namespace UniShare.Infrastructure;

internal static partial class TikTokMetadata
{
    internal static WebCaptureMetadata? Extract(string html, Uri sourceUri)
    {
        var id = OriginContentRules.TikTokPostId(sourceUri);
        if (id is null)
        {
            return null;
        }
        foreach (Match script in ScriptRegex().Matches(html))
        {
            var attribute = ScriptIdRegex().Match(script.Groups["attributes"].Value);
            var scriptId = attribute.Groups["value"].Value;
            if (scriptId is not ("__UNIVERSAL_DATA_FOR_REHYDRATION__" or "SIGI_STATE" or "sigi-persisted-data"))
            {
                continue;
            }
            try
            {
                using var document = JsonDocument.Parse(script.Groups["json"].Value);
                var items = scriptId == "__UNIVERSAL_DATA_FOR_REHYDRATION__"
                    ? new[] {
                        At(document.RootElement, "__DEFAULT_SCOPE__", "webapp.video-detail", "itemInfo", "itemStruct"),
                        At(document.RootElement, "__DEFAULT_SCOPE__", "webapp.reflow.video.detail", "itemInfo", "itemStruct"),
                    }
                    : new[] { At(document.RootElement, "ItemModule", id) };
                foreach (var item in items)
                {
                    if (item is not { ValueKind: JsonValueKind.Object } post || Text(post, "id") != id ||
                        At(post, "desc") is not { ValueKind: JsonValueKind.String } caption)
                    {
                        continue;
                    }
                    var author = At(post, "author") ?? At(post, "authorInfo");
                    var authorName = author is { ValueKind: JsonValueKind.String } name
                        ? name.GetString()
                        : author is { ValueKind: JsonValueKind.Object } person
                            ? Text(person, "nickname") ?? Text(person, "uniqueId")
                            : null;
                    var explicitTitle = OriginContentRules.LimitField(Text(post, "title"), 500) ??
                        (At(post, "imagePost") is { ValueKind: JsonValueKind.Object } imagePost ? Text(imagePost, "title") : null);
                    return FromCaption(caption.GetString(), authorName, explicitTitle);
                }
            }
            catch (JsonException)
            {
                // Public HTML can be incomplete; never use a different object's description.
            }
        }
        return null;
    }

    internal static Uri? OEmbedUri(Uri sourceUri)
    {
        var id = OriginContentRules.TikTokPostId(sourceUri);
        if (id is null)
        {
            return null;
        }
        var publicUri = new UriBuilder(sourceUri)
        {
            Scheme = "https",
            Host = "www.tiktok.com",
            Port = -1,
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        }.Uri;
        return new Uri($"https://www.tiktok.com/oembed?url={Uri.EscapeDataString(publicUri.AbsoluteUri)}");
    }

    internal static WebCaptureMetadata? ReadOEmbed(string json, Uri sourceUri)
    {
        var id = OriginContentRules.TikTokPostId(sourceUri);
        if (id is null)
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (Text(root, "provider_name") != "TikTok" || Text(root, "type") != "video")
            {
                return null;
            }
            var embedId = Text(root, "embed_product_id");
            var html = Text(root, "html") ?? string.Empty;
            if (embedId != id && !(embedId is null && EmbedIdRegex().Matches(html).Any(match => match.Groups["id"].Value == id)))
            {
                return null;
            }
            var caption = Text(root, "title");
            return caption is null ? null : FromCaption(caption, Text(root, "author_name"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static WebCaptureMetadata FromCaption(string? value, string? author, string? explicitTitle = null)
    {
        var caption = OriginContentRules.NormalizeCaption(value);
        var title = string.IsNullOrWhiteSpace(explicitTitle) ? LinkCaptureDefaults.TitleFromCaption(caption) : explicitTitle;
        return new WebCaptureMetadata(OriginContentRules.LimitField(title, 500), "TikTok",
            OriginContentRules.LimitField(author, 500), caption, OriginDescriptionFound: true);
    }

    private static string? Text(JsonElement value, string property) =>
        At(value, property) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;

    private static JsonElement? At(JsonElement value, params string[] path)
    {
        foreach (var step in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(step, out value))
            {
                return null;
            }
        }
        return value;
    }

    [GeneratedRegex(@"<script\b(?<attributes>[^>]*)>(?<json>.*?)</script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex("""\bid\s*=\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)'|(?<value>[^\s>]+))""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptIdRegex();

    [GeneratedRegex("""<blockquote\b[^>]*\bdata-video-id\s*=\s*["'](?<id>[0-9]+)["']""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmbedIdRegex();
}
