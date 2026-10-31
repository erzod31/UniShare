package org.unishare.app

import java.net.URI
import java.net.URLDecoder
import org.json.JSONArray
import org.json.JSONObject
import org.json.JSONTokener

internal object OriginContentRules {
    fun titleFromCaption(value: String?): String? {
        val first = firstCaptionLine(value) ?: return null
        var text = first.replace(Regex("[\\s\\p{Z}]+"), " ")
        val tail = Regex("\\s+(?:#\\S+|https?://\\S+)(?:\\s+(?:#\\S+|https?://\\S+))*\\s*$", RegexOption.IGNORE_CASE).find(text)
        if (tail != null && text.codePointCount(0, tail.range.first) >= 12 &&
            (tail.value.trim().split(' ').size > 1 || tail.value.contains("://"))
        ) text = text.substring(0, tail.range.first)
        if (text.codePointCount(0, text.length) <= 80) return text
        var prefix = text.substring(0, text.offsetByCodePoints(0, 79))
        val boundary = prefix.lastIndexOf(' ')
        if (boundary > 0 && prefix.codePointCount(0, boundary) >= 40) prefix = prefix.substring(0, boundary)
        return prefix.trimEnd() + "…"
    }

    fun legacyTitleFromCaption(value: String?): String? = limitField(firstCaptionLine(value), 500)

    private fun firstCaptionLine(value: String?): String? =
        value?.replace("\r\n", "\n")?.replace('\r', '\n')?.trim()?.substringBefore('\n')?.trim()?.ifBlank { null }

    fun limitField(value: String?, maximumLength: Int): String? {
        val text = value?.trim()?.ifBlank { null } ?: return null
        if (text.length <= maximumLength) return text
        val end = if (Character.isHighSurrogate(text[maximumLength - 1])) maximumLength - 1 else maximumLength
        return text.substring(0, end)
    }

    fun normalizeCaption(value: String?): String? {
        val text = value?.replace("\r\n", "\n")?.replace('\r', '\n')?.trim()?.ifBlank { null } ?: return null
        return if (text.codePointCount(0, text.length) <= 4000) text else text.substring(0, text.offsetByCodePoints(0, 3999)) + "…"
    }

    fun tikTokPostId(uri: URI): String? {
        val host = uri.host.orEmpty().trimEnd('.')
        if (uri.scheme !in setOf("http", "https") || !uri.userInfo.isNullOrEmpty() ||
            !(host.equals("tiktok.com", true) || host.endsWith(".tiktok.com", true))
        ) return null
        return TIKTOK_PATH.matchEntire(uri.path.orEmpty())?.groupValues?.get(1)
    }

    fun matchesContentUrl(value: String?, sourceUri: URI): Boolean {
        if (value.isNullOrBlank()) return true
        return runCatching {
            val target = sourceUri.resolve(value)
            target.scheme in setOf("http", "https") && normalizeUrl(target) == normalizeUrl(sourceUri)
        }.getOrDefault(false)
    }

    private fun normalizeUrl(uri: URI): String {
        require(uri.userInfo.isNullOrEmpty())
        val query = uri.rawQuery.orEmpty().split('&').filter { pair ->
            val name = URLDecoder.decode(pair.substringBefore('='), "UTF-8").lowercase()
            pair.isNotEmpty() && !name.startsWith("utm_") && name !in TRACKING_PARAMETERS
        }.joinToString("&")
        val port = if (uri.port < 0 || (uri.scheme == "https" && uri.port == 443) ||
            (uri.scheme == "http" && uri.port == 80)
        ) "" else ":${uri.port}"
        val path = uri.rawPath.orEmpty().trimEnd('/').ifEmpty { "/" }
        return "${uri.scheme.lowercase()}://${uri.host.lowercase()}$port$path" + if (query.isEmpty()) "" else "?$query"
    }

    fun parseJson(value: String): Any? {
        if (value.length > 2 * 1024 * 1024) return null
        var depth = 0
        var quoted = false
        var escaped = false
        for (character in value) {
            if (quoted) {
                if (escaped) escaped = false else if (character == '\\') escaped = true
                else if (character == '"') quoted = false
            } else when (character) {
                '"' -> quoted = true
                '{', '[' -> if (++depth > 64) return null
                '}', ']' -> if (--depth < 0) return null
            }
        }
        if (quoted || depth != 0) return null
        return runCatching {
            val tokener = JSONTokener(value)
            val root = tokener.nextValue()
            root.takeIf { (it is JSONObject || it is JSONArray) && tokener.nextClean() == '\u0000' }
        }.getOrNull()
    }

    private val TRACKING_PARAMETERS = setOf("fbclid", "gclid", "mc_cid", "mc_eid")
    private val TIKTOK_PATH = Regex("^/(?:@[^/]+/(?:video|photo)|share/video|embed(?:/v2)?|player/v1|v)/([0-9]{10,25})(?:\\.html)?/?$")
}
