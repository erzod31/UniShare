package org.unishare.app

import java.net.URI
import java.net.URLEncoder
import org.json.JSONObject

internal object TikTokMetadata {
    fun extract(html: String, sourceUri: URI): LinkMetadata? {
        val id = OriginContentRules.tikTokPostId(sourceUri) ?: return null
        for (script in SCRIPT.findAll(html)) {
            val attribute = SCRIPT_ID.find(script.groupValues[1]) ?: continue
            val scriptId = attribute.groupValues.drop(1).firstOrNull(String::isNotEmpty) ?: continue
            if (scriptId !in setOf("__UNIVERSAL_DATA_FOR_REHYDRATION__", "SIGI_STATE", "sigi-persisted-data")) continue
            val root = OriginContentRules.parseJson(script.groupValues[2]) as? JSONObject ?: continue
            val items = if (scriptId == "__UNIVERSAL_DATA_FOR_REHYDRATION__") {
                listOf(
                    at(root, "__DEFAULT_SCOPE__", "webapp.video-detail", "itemInfo", "itemStruct"),
                    at(root, "__DEFAULT_SCOPE__", "webapp.reflow.video.detail", "itemInfo", "itemStruct"),
                )
            } else listOf(at(root, "ItemModule", id))
            for (item in items) {
                val post = item as? JSONObject ?: continue
                if (post.opt("id") != id || post.opt("desc") !is String) continue
                val author = post.opt("author").takeUnless { it == JSONObject.NULL } ?: post.opt("authorInfo")
                val authorName = when (author) {
                    is String -> author
                    is JSONObject -> (author.opt("nickname") as? String) ?: (author.opt("uniqueId") as? String)
                    else -> null
                }
                val explicitTitle = (post.opt("title") as? String)?.ifBlank { null } ?: (post.optJSONObject("imagePost")?.opt("title") as? String)
                return fromCaption(post.getString("desc"), authorName, explicitTitle)
            }
        }
        return null
    }

    fun oEmbedUri(sourceUri: URI): URI? {
        OriginContentRules.tikTokPostId(sourceUri) ?: return null
        val publicUri = URI("https", null, "www.tiktok.com", -1, sourceUri.path, null, null)
        return URI("https://www.tiktok.com/oembed?url=${URLEncoder.encode(publicUri.toASCIIString(), "UTF-8")}")
    }

    fun readOEmbed(json: String, sourceUri: URI): LinkMetadata? {
        val id = OriginContentRules.tikTokPostId(sourceUri) ?: return null
        val root = OriginContentRules.parseJson(json) as? JSONObject ?: return null
        if (root.opt("provider_name") != "TikTok" || root.opt("type") != "video") return null
        val embedId = root.opt("embed_product_id") as? String
        val html = root.opt("html") as? String ?: ""
        if (embedId != id && !(embedId == null && EMBED_ID.findAll(html).any { it.groupValues[1] == id })) return null
        val caption = root.opt("title") as? String ?: return null
        return fromCaption(caption, root.opt("author_name") as? String)
    }

    private fun fromCaption(value: String?, author: String?, explicitTitle: String? = null): LinkMetadata {
        val caption = OriginContentRules.normalizeCaption(value)
        val title = explicitTitle?.ifBlank { null } ?: OriginContentRules.titleFromCaption(caption)
        return LinkMetadata(OriginContentRules.limitField(title, 500), "TikTok",
            OriginContentRules.limitField(author, 500), caption, originDescriptionFound = true)
    }

    private fun at(root: JSONObject, vararg path: String): Any? {
        var value: Any? = root
        for (step in path) value = (value as? JSONObject)?.opt(step)
        return value
    }

    private val SCRIPT = Regex("<script\\b([^>]*)>(.*?)</script\\s*>", setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL))
    private val SCRIPT_ID = Regex("\\bid\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOption.IGNORE_CASE)
    private val EMBED_ID = Regex("<blockquote\\b[^>]*\\bdata-video-id\\s*=\\s*[\"']([0-9]+)[\"']", RegexOption.IGNORE_CASE)
}
