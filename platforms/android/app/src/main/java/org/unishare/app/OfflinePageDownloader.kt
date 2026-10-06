package org.unishare.app

import android.content.Context
import java.io.File
import java.io.FileOutputStream
import java.net.HttpURLConnection
import java.net.InetAddress
import java.net.URI
import java.net.URL
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.security.MessageDigest
import java.util.UUID

data class DownloadedContent(
    val file: File,
    val sha256: String,
    val byteLength: Long,
    val mimeType: String,
    val originalName: String,
    val metadata: LinkMetadata? = null,
)

data class LinkMetadata(
    val title: String?,
    val source: String?,
    val author: String?,
    val description: String?,
    val promotionalDescription: String? = null,
) {
    fun resolveDescription(currentDescription: String?): String? =
        if (currentDescription.isNullOrBlank() ||
            isKnownNavigationJunk(currentDescription) ||
            (!promotionalDescription.isNullOrBlank() &&
                currentDescription.trim() == promotionalDescription.trim())
        ) description else currentDescription

    companion object {
        internal fun isKnownNavigationJunk(value: String?): Boolean {
            if (value.isNullOrBlank()) return false
            val normalized = value.trim().replace(Regex("\\s+"), " ").uppercase()
            val fragments = listOf(
                "AUTEURSRECHT", "CONTACTCREATORS", "ADVERTEREN", "ONTWIKKELAARS", "VOORWAARDEN",
                "PRIVACYBELEID", "ZO WERKT YOUTUBE", "NIEUWE FUNCTIES TESTEN",
                "COPYRIGHT", "CONTACT US", "ADVERTISE", "DEVELOPERS", "TERMS", "PRIVACY POLICY",
                "DERECHOS DE AUTOR", "CONTACTAR", "CREADORES", "PUBLICIDAD", "DESARROLLADORES",
                "TÉRMINOS", "PRIVACIDAD", "CÓMO FUNCIONA YOUTUBE",
            )
            val compactNavigation = fragments.count(normalized::contains) >= 4 &&
                (value.count(Char::isWhitespace) * 20 < value.length ||
                    Regex("(?<=[a-zà-ÿ])(?=[A-Z])").findAll(value).count() >= 3)
            return compactNavigation ||
                normalized.contains("OVERPERSAUTEURSRECHTCONTACTCREATORS") ||
                normalized.contains("ABOUTPRESSCOPYRIGHTCONTACTCREATORS") ||
                normalized.contains("ACERCADEPRENSADERECHOSDEAUTORCONTACTARCREADORES")
        }
    }
}

class OfflinePageDownloader(private val context: Context) {
    fun fetchMetadata(rawUrl: String): LinkMetadata? {
        var uri = URI(rawUrl.trim())
        repeat(MAXIMUM_REDIRECTS + 1) { redirectCount ->
            require(SharePayload.isWebUrl(uri.toASCIIString())) { "El enlace no es válido" }
            OfflineNetworkPolicy.requirePublic(uri)
            val connection = (URL(uri.toASCIIString()).openConnection() as HttpURLConnection).apply {
                requestMethod = "GET"
                connectTimeout = 15_000
                readTimeout = 30_000
                instanceFollowRedirects = false
                setRequestProperty("Accept", "text/html,application/xhtml+xml;q=0.9")
                setRequestProperty("User-Agent", "UniShare-Android/${BuildConfig.VERSION_NAME}")
            }
            try {
                when (val code = connection.responseCode) {
                    in REDIRECT_CODES -> {
                        require(redirectCount < MAXIMUM_REDIRECTS) { "La página tiene demasiadas redirecciones" }
                        val location = connection.getHeaderField("Location")
                            ?: throw IllegalStateException("La redirección no indica un destino")
                        uri = uri.resolve(location)
                    }
                    HttpURLConnection.HTTP_OK -> {
                        val mimeType = connection.contentType?.substringBefore(';')?.trim()?.lowercase()
                        if (mimeType !in setOf("text/html", "application/xhtml+xml")) return null
                        require(connection.contentLengthLong < 0 || connection.contentLengthLong <= MAXIMUM_METADATA_BYTES) {
                            "Los metadatos de la página superan el límite"
                        }
                        val output = java.io.ByteArrayOutputStream()
                        connection.inputStream.use { input ->
                            val buffer = ByteArray(32 * 1024)
                            while (true) {
                                val count = input.read(buffer)
                                if (count < 0) break
                                require(output.size() + count <= MAXIMUM_METADATA_BYTES) {
                                    "Los metadatos de la página superan el límite"
                                }
                                output.write(buffer, 0, count)
                            }
                        }
                        return OfflineHtmlSnapshot.extractMetadata(
                            output.toString(Charsets.UTF_8.name()), uri,
                        )
                    }
                    else -> throw IllegalStateException("El servidor respondió con HTTP $code")
                }
            } finally {
                connection.disconnect()
            }
        }
        return null
    }

    fun download(rawUrl: String): DownloadedContent {
        var uri = URI(rawUrl.trim())
        repeat(MAXIMUM_REDIRECTS + 1) { redirectCount ->
            require(SharePayload.isWebUrl(uri.toASCIIString())) { "El enlace de descarga no es válido" }
            OfflineNetworkPolicy.requirePublic(uri)
            val connection = (URL(uri.toASCIIString()).openConnection() as HttpURLConnection).apply {
                requestMethod = "GET"
                connectTimeout = 15_000
                readTimeout = 30_000
                instanceFollowRedirects = false
                setRequestProperty("Accept", "text/html,application/xhtml+xml,application/pdf,*/*;q=0.8")
                setRequestProperty("User-Agent", "UniShare-Android/${BuildConfig.VERSION_NAME}")
            }
            try {
                when (val code = connection.responseCode) {
                    in REDIRECT_CODES -> {
                        require(redirectCount < MAXIMUM_REDIRECTS) { "La descarga tiene demasiadas redirecciones" }
                        val location = connection.getHeaderField("Location")
                            ?: throw IllegalStateException("La redirección no indica un destino")
                        uri = uri.resolve(location)
                    }
                    HttpURLConnection.HTTP_OK -> return readResponse(connection, uri)
                    else -> throw IllegalStateException("El servidor respondió con HTTP $code")
                }
            } finally {
                connection.disconnect()
            }
        }
        throw IllegalStateException("La descarga no pudo completarse")
    }

    private fun readResponse(connection: HttpURLConnection, sourceUri: URI): DownloadedContent {
        val announced = connection.contentLengthLong
        require(announced < 0 || announced <= MAXIMUM_DOWNLOAD_BYTES) {
            "La descarga supera el límite de 256 MiB"
        }
        val staging = File(context.filesDir, "staging").apply { mkdirs() }
        val partial = File(staging, "${UUID.randomUUID()}.partial")
        try {
            var length = 0L
            connection.inputStream.use { input ->
                FileOutputStream(partial).use { fileOutput ->
                    val output = fileOutput.buffered()
                    val buffer = ByteArray(128 * 1024)
                    while (true) {
                        val count = input.read(buffer)
                        if (count < 0) break
                        length += count
                        require(length <= MAXIMUM_DOWNLOAD_BYTES) { "La descarga supera el límite de 256 MiB" }
                        output.write(buffer, 0, count)
                    }
                    output.flush()
                    fileOutput.fd.sync()
                }
            }

            var mimeType = connection.contentType?.substringBefore(';')?.trim()?.lowercase()
                ?.takeIf { it.isNotEmpty() } ?: "application/octet-stream"
            var name = OfflineDownloadNames.fromUri(sourceUri, mimeType)
            var metadata: LinkMetadata? = null
            if (mimeType == "text/html" || mimeType == "application/xhtml+xml") {
                metadata = OfflineHtmlSnapshot.rewrite(partial, sourceUri)
                mimeType = "text/html"
                name = "${name.substringBeforeLast('.', name)}.offline.html"
            }
            val (sha256, finalLength) = hashAndLength(partial)
            return DownloadedContent(partial, sha256, finalLength, mimeType, name, metadata)
        } catch (error: Exception) {
            partial.delete()
            throw error
        }
    }

    private fun hashAndLength(file: File): Pair<String, Long> {
        val digest = MessageDigest.getInstance("SHA-256")
        var length = 0L
        file.inputStream().buffered().use { input ->
            val buffer = ByteArray(128 * 1024)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                length += count
                digest.update(buffer, 0, count)
            }
        }
        return digest.digest().joinToString("") { "%02x".format(it.toInt() and 0xff) } to length
    }

    companion object {
        private const val MAXIMUM_REDIRECTS = 5
        private const val MAXIMUM_DOWNLOAD_BYTES = 256L * 1024 * 1024
        private const val MAXIMUM_METADATA_BYTES = 2 * 1024 * 1024
        private val REDIRECT_CODES = setOf(301, 302, 303, 307, 308)
    }
}

object OfflineNetworkPolicy {
    fun requirePublic(uri: URI) {
        val addresses = InetAddress.getAllByName(uri.host)
        require(addresses.isNotEmpty() && addresses.none(::isUnsafe)) {
            "UniShare no descarga páginas desde direcciones privadas o locales"
        }
    }

    fun isUnsafe(address: InetAddress): Boolean {
        if (address.isAnyLocalAddress || address.isLoopbackAddress || address.isLinkLocalAddress ||
            address.isSiteLocalAddress || address.isMulticastAddress
        ) return true
        val bytes = address.address
        return bytes.size == 16 && (bytes[0].toInt() and 0xfe) == 0xfc
    }
}

object OfflineDownloadNames {
    fun fromUri(uri: URI, mimeType: String): String {
        val raw = uri.path?.substringAfterLast('/')?.takeIf { it.isNotBlank() }
            ?: if (mimeType == "text/html") "pagina.html" else "descarga"
        val safe = raw.replace(Regex("[^A-Za-z0-9._-]"), "_").take(180).trim('.', '_')
        return safe.ifEmpty { "descarga" }
    }
}

object OfflineHtmlSnapshot {
    fun rewrite(file: File, sourceUri: URI): LinkMetadata {
        require(file.length() <= MAXIMUM_HTML_BYTES) { "La página HTML supera el límite de 16 MiB" }
        val source = file.readText(Charsets.UTF_8)
        val metadata = extractMetadata(source, sourceUri)
        val title = metadata.title ?: sourceUri.host
        val withoutActiveContent = ACTIVE_CONTENT.replace(source, "")
        val withLines = BLOCK_TAGS.replace(withoutActiveContent, "\n")
        val text = plainText(ALL_TAGS.replace(withLines, ""))
        val replacement = File(file.parentFile, "${file.name}.${UUID.randomUUID()}.rewrite")
        try {
            FileOutputStream(replacement).use { output ->
                output.write(buildDocument(title, sourceUri, text).toByteArray(Charsets.UTF_8))
                output.fd.sync()
            }
            Files.move(
                replacement.toPath(),
                file.toPath(),
                StandardCopyOption.ATOMIC_MOVE,
                StandardCopyOption.REPLACE_EXISTING,
            )
        } finally {
            replacement.delete()
        }
        return metadata
    }

    fun extractMetadata(source: String, sourceUri: URI): LinkMetadata {
        val documentTitle = TITLE.find(source)?.groupValues?.get(1)?.let(::plainText)
            ?.take(500)?.ifBlank { null }
        val values = mutableMapOf<String, String>()
        META_TAG.findAll(source).forEach { tag ->
            val attributes = ATTRIBUTE.findAll(tag.value).associate { attribute ->
                val value = attribute.groups[2]?.value?.ifEmpty { null }
                    ?: attribute.groups[3]?.value.orEmpty()
                attribute.groups[1]!!.value.lowercase() to plainText(value)
            }
            val key = attributes["name"] ?: attributes["property"]
            val content = attributes["content"]
            if (!key.isNullOrBlank() && !content.isNullOrBlank()) values.putIfAbsent(key.lowercase(), content)
        }
        val promotionalDescription = values["og:description"] ?: values["twitter:description"]
            ?: values["description"]
        val originTitle = values["og:title"] ?: values["twitter:title"] ?: documentTitle
        return LinkMetadata(
            originTitle?.take(500),
            (values["og:site_name"] ?: values["application-name"] ?: sourceUri.host).take(500),
            (values["author"] ?: values["article:author"])?.take(500),
            extractContentSummary(source, originTitle, sourceUri),
            promotionalDescription?.take(4000),
        )
    }

    private fun extractContentSummary(source: String, title: String?, sourceUri: URI): String? {
        extractStructuredArticleBody(source)?.let { body ->
            buildSummary(listOf(body), title)?.let { return it }
        }
        if (isYouTubeVideoUri(sourceUri)) {
            extractYouTubeVideoDescription(source)?.let { description ->
                buildSummary(listOf(description), title)?.let { return it }
            }
        }
        extractStructuredDescription(source)?.let { description ->
            buildSummary(listOf(description), title)?.let { return it }
        }
        var cleaned = ACTIVE_CONTENT.replace(source, "")
        cleaned = NON_CONTENT_REGION.replace(cleaned, "")
        val semanticScope = selectContentScope(cleaned)
        val scope = semanticScope ?: BODY_REGION.find(cleaned)?.groupValues?.get(1)
            ?: cleaned
        val paragraphs = extractParagraphs(scope).toMutableList()
        if (paragraphs.isEmpty() && semanticScope != null) {
            val withLines = BLOCK_TAGS.replace(scope, "\n")
            paragraphs += plainText(ALL_TAGS.replace(withLines, "")).lines()
        }
        return buildSummary(paragraphs, title)
    }

    private fun selectContentScope(source: String): String? =
        (ARTICLE_REGION.findAll(source) + MAIN_REGION.findAll(source))
            .map { it.groupValues[1] }
            .map { value -> value to extractParagraphs(value).sumOf(String::length) }
            .sortedWith(compareByDescending<Pair<String, Int>> { it.second }.thenByDescending { it.first.length })
            .firstOrNull()?.first

    private fun extractParagraphs(scope: String): List<String> =
        PARAGRAPH.findAll(scope).mapNotNull { match ->
            val raw = match.groupValues[1]
            val text = plainText(ALL_TAGS.replace(raw, " ")).replace('\n', ' ')
            if (text.length < MINIMUM_USEFUL_TEXT_LENGTH) return@mapNotNull null
            val linkedCharacters = ANCHOR.findAll(raw).sumOf { link ->
                plainText(ALL_TAGS.replace(link.groupValues[1], " ")).length
            }
            text.takeUnless { linkedCharacters * 2 > text.length || looksLikeBoilerplate(it) }
        }.toList()

    private fun extractStructuredArticleBody(source: String): String? {
        JSON_LD_SCRIPT.findAll(source).forEach { script ->
            ARTICLE_BODY_JSON.find(script.groupValues[1])?.groupValues?.get(1)?.let { encoded ->
                return plainText(unescapeJsonString(encoded))
            }
        }
        return null
    }

    private fun extractStructuredDescription(source: String): String? {
        JSON_LD_SCRIPT.findAll(source).forEach { script ->
            val json = script.groupValues[1]
            val hasSupportedType = CONTENT_TYPE_JSON.findAll(json).any { typeMatch ->
                typeMatch.groupValues.drop(1).joinToString(" ").split(Regex("[^A-Za-z]+"))
                    .any(SUPPORTED_CONTENT_TYPES::contains)
            }
            if (!hasSupportedType) return@forEach
            DESCRIPTION_JSON.find(json)?.groupValues?.get(1)?.let { encoded ->
                return plainText(unescapeJsonString(encoded))
            }
        }
        return null
    }

    private fun extractYouTubeVideoDescription(source: String): String? =
        YOUTUBE_SHORT_DESCRIPTION.find(source)?.groupValues?.get(1)
            ?.let(::unescapeJsonString)?.let(::plainText)?.ifBlank { null }

    private fun isYouTubeVideoUri(uri: URI): Boolean {
        val host = uri.host.orEmpty().trimEnd('.')
        val youtubeHost = host.equals("youtu.be", ignoreCase = true) ||
            host.equals("youtube.com", ignoreCase = true) || host.endsWith(".youtube.com", ignoreCase = true)
        return youtubeHost && (host.equals("youtu.be", ignoreCase = true) ||
            uri.path.equals("/watch", ignoreCase = true) ||
            uri.path.startsWith("/shorts/", ignoreCase = true) ||
            uri.path.startsWith("/live/", ignoreCase = true))
    }

    private fun unescapeJsonString(value: String): String = JSON_ESCAPE.replace(value) { match ->
        when (val escaped = match.groupValues[1]) {
            "\"" -> "\""
            "\\" -> "\\"
            "/" -> "/"
            "b" -> "\b"
            "f" -> "\u000C"
            "n" -> "\n"
            "r" -> "\r"
            "t" -> "\t"
            else -> escaped.drop(1).toIntOrNull(16)?.toChar()?.toString() ?: match.value
        }
    }

    private fun buildSummary(paragraphs: Iterable<String>, title: String?): String? {
        val normalizedTitle = normalizeForComparison(title)
        val sentences = paragraphs.flatMap { paragraph ->
            plainText(paragraph).lines().flatMap { line ->
                var cleaned = line.trim()
                if (!title.isNullOrBlank() && cleaned.startsWith(title, ignoreCase = true)) {
                    cleaned = cleaned.drop(title.length).trimStart(' ', '-', '–', '—', ':', '|')
                }
                if (cleaned.length < 35 || looksLikeBoilerplate(cleaned) || !looksLikeProse(cleaned) ||
                    normalizeForComparison(cleaned) == normalizedTitle ||
                    cleaned.startsWith("http://", ignoreCase = true) ||
                    cleaned.startsWith("https://", ignoreCase = true)
                ) emptyList() else SENTENCE_BOUNDARY.split(cleaned).map(String::trim).filter { sentence ->
                    sentence.length >= 35 && looksLikeProse(sentence) &&
                        normalizeForComparison(sentence) != normalizedTitle
                }
            }
        }
        if (sentences.isEmpty()) return null
        val summary = StringBuilder()
        for (sentence in sentences.take(4)) {
            val separatorLength = if (summary.isEmpty()) 0 else 1
            if (summary.length + separatorLength + sentence.length > MAXIMUM_SUMMARY_LENGTH) {
                if (summary.isEmpty()) return clipAtWord(sentence, MAXIMUM_SUMMARY_LENGTH)
                break
            }
            if (summary.isNotEmpty()) summary.append(' ')
            summary.append(sentence)
            if (summary.length >= 260) break
        }
        return summary.toString().ifBlank { null }
    }

    private fun clipAtWord(value: String, maximumLength: Int): String {
        if (value.length <= maximumLength) return value
        val boundary = value.lastIndexOf(' ', maximumLength - 1)
        return value.substring(0, if (boundary > 80) boundary else maximumLength - 1).trimEnd() + "…"
    }

    private fun normalizeForComparison(value: String?): String = value.orEmpty().trim()
        .replace(Regex("\\s+"), " ").uppercase()

    private fun looksLikeBoilerplate(value: String): Boolean {
        val normalized = normalizeForComparison(value)
        return normalized.contains("POLÍTICA DE COOKIES") || normalized.contains("COOKIE POLICY") ||
            normalized.contains("TODOS LOS DERECHOS RESERVADOS") ||
            normalized.contains("ALL RIGHTS RESERVED") || normalized.startsWith("ACEPTAR COOKIES") ||
            normalized.startsWith("INICIAR SESIÓN") || normalized.startsWith("SIGN IN") ||
            LinkMetadata.isKnownNavigationJunk(value)
    }

    private fun looksLikeProse(value: String): Boolean {
        val letters = value.count(Char::isLetter)
        if (letters < 25) return false
        val latinLetters = value.count { character ->
            character in 'A'..'Z' || character in 'a'..'z' || character in '\u00C0'..'\u024F'
        }
        if (latinLetters * 4 < letters * 3) return true
        val whitespace = value.count(Char::isWhitespace)
        val camelTransitions = CAMEL_CASE_BOUNDARY.findAll(value).count()
        return whitespace >= 5 && whitespace * 18 >= value.length && camelTransitions <= 3
    }

    private fun plainText(value: String): String {
        val decoded = NUMERIC_HTML_ENTITY.replace(value) { match ->
            val raw = match.groupValues[1]
            val codePoint = if (raw.startsWith('x', ignoreCase = true)) {
                raw.drop(1).toIntOrNull(16)
            } else {
                raw.toIntOrNull()
            }
            codePoint?.takeIf(Character::isValidCodePoint)?.let { String(Character.toChars(it)) }
                ?: match.value
        }.replace("&nbsp;", " ", ignoreCase = true)
            .replace("&amp;", "&", ignoreCase = true)
            .replace("&lt;", "<", ignoreCase = true)
            .replace("&gt;", ">", ignoreCase = true)
            .replace("&quot;", "\"", ignoreCase = true)
            .replace("&apos;", "'", ignoreCase = true)
            .replace("&hellip;", "…", ignoreCase = true)
            .replace("&ndash;", "–", ignoreCase = true)
            .replace("&mdash;", "—", ignoreCase = true)
        return decoded.replace(Regex("[\\t\\f\\u000B ]+"), " ")
            .lines().map(String::trim).filter(String::isNotEmpty).joinToString("\n")
    }

    private fun escape(value: String): String = value.replace("&", "&amp;")
        .replace("<", "&lt;").replace(">", "&gt;").replace("\"", "&quot;")

    private fun buildDocument(title: String, sourceUri: URI, text: String): String = """
        <!doctype html><html lang="es"><head><meta charset="utf-8">
        <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src 'none'; media-src 'none'; frame-src 'none'; connect-src 'none'; script-src 'none'; style-src 'unsafe-inline'">
        <meta name="viewport" content="width=device-width, initial-scale=1"><title>${escape(title)}</title>
        <style>body{max-width:76ch;margin:3rem auto;padding:0 1.25rem;font:18px/1.65 system-ui,sans-serif;color:#20242b;background:#fff}header{border-bottom:1px solid #ddd;margin-bottom:2rem}pre{white-space:pre-wrap;font:inherit}.source{color:#59636f;overflow-wrap:anywhere}</style></head>
        <body><header><h1>${escape(title)}</h1><p class="source">Origen: ${escape(sourceUri.toASCIIString())}</p></header>
        <main><pre>${escape(text)}</pre></main></body></html>
    """.trimIndent()

    private const val MAXIMUM_HTML_BYTES = 16L * 1024 * 1024
    private const val MAXIMUM_SUMMARY_LENGTH = 600
    private const val MINIMUM_USEFUL_TEXT_LENGTH = 60
    private val ACTIVE_CONTENT = Regex(
        "<(script|style|noscript|iframe|object|embed)\\b[^>]*>.*?</\\1\\s*>",
        setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL),
    )
    private val TITLE = Regex("<title\\b[^>]*>(.*?)</title\\s*>", setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL))
    private val NON_CONTENT_REGION = Regex(
        "<(nav|aside|footer|header|form|dialog|menu|svg|canvas)\\b[^>]*>.*?</\\1\\s*>",
        setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL),
    )
    private val JSON_LD_SCRIPT = Regex(
        "<script\\b[^>]*type\\s*=\\s*['\"]application/ld\\+json['\"][^>]*>(.*?)</script\\s*>",
        setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL),
    )
    private val ARTICLE_BODY_JSON = Regex(
        "\\\"articleBody\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"\\\\])*)\\\"",
        setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL),
    )
    private val DESCRIPTION_JSON = Regex(
        "\\\"description\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"\\\\])*)\\\"",
        setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL),
    )
    private val CONTENT_TYPE_JSON = Regex(
        "\\\"@type\\\"\\s*:\\s*(?:\\\"([^\\\"]+)\\\"|\\[([^]]+)])",
        setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL),
    )
    private val YOUTUBE_SHORT_DESCRIPTION = Regex(
        "\\\"shortDescription\\\"\\s*:\\s*\\\"((?:\\\\.|[^\\\"\\\\])*)\\\"",
        setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL),
    )
    private val JSON_ESCAPE = Regex("\\\\(u[0-9a-fA-F]{4}|[\\\"\\\\/bfnrt])")
    private val NUMERIC_HTML_ENTITY = Regex("&#(x[0-9a-fA-F]+|[0-9]+);", RegexOption.IGNORE_CASE)
    private val ARTICLE_REGION = Regex(
        "<article\\b[^>]*>(.*?)</article\\s*>",
        setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL),
    )
    private val MAIN_REGION = Regex(
        "<main\\b[^>]*>(.*?)</main\\s*>",
        setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL),
    )
    private val BODY_REGION = Regex(
        "<body\\b[^>]*>(.*?)</body\\s*>",
        setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL),
    )
    private val PARAGRAPH = Regex(
        "<p\\b[^>]*>(.*?)</p\\s*>",
        setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL),
    )
    private val ANCHOR = Regex(
        "<a\\b[^>]*>(.*?)</a\\s*>",
        setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL),
    )
    private val SENTENCE_BOUNDARY = Regex("(?<=[.!?…])\\s+")
    private val CAMEL_CASE_BOUNDARY = Regex("(?<=[a-zà-ÿ])(?=[A-Z])")
    private val META_TAG = Regex("<meta\\b[^>]*>", RegexOption.IGNORE_CASE)
    private val ATTRIBUTE = Regex("([A-Za-z_:][-A-Za-z0-9_:.]*)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)')")
    private val BLOCK_TAGS = Regex("</?(?:p|div|br|li|h[1-6]|tr|section|article|header|footer)\\b[^>]*>", RegexOption.IGNORE_CASE)
    private val ALL_TAGS = Regex("<[^>]+>", RegexOption.DOT_MATCHES_ALL)
    private val SUPPORTED_CONTENT_TYPES = setOf(
        "Article", "NewsArticle", "BlogPosting", "TechArticle", "Report", "ScholarlyArticle",
        "VideoObject", "AudioObject", "PodcastEpisode",
    )
}
