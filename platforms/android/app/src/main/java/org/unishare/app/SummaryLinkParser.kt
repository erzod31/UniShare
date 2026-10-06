package org.unishare.app

data class SummaryLink(
    val url: String,
    val start: Int,
    val endExclusive: Int,
)

object SummaryLinkParser {
    private val webLink = Regex("https?://[^\\s<>\\\"']+", RegexOption.IGNORE_CASE)

    fun find(text: String?): List<SummaryLink> {
        if (text.isNullOrEmpty()) return emptyList()

        return webLink.findAll(text).mapNotNull { match ->
            val link = trimTrailingPunctuation(match.value)
            if (link.isEmpty()) return@mapNotNull null

            val uri = runCatching { java.net.URI(link) }.getOrNull()
                ?: return@mapNotNull null
            if (uri.scheme?.lowercase() !in setOf("http", "https") || uri.host.isNullOrBlank()) {
                return@mapNotNull null
            }

            SummaryLink(link, match.range.first, match.range.first + link.length)
        }.toList()
    }

    private fun trimTrailingPunctuation(value: String): String {
        var length = value.length
        while (length > 0) {
            val character = value[length - 1]
            if (character in ".,;:!?") {
                length--
                continue
            }

            if (character == ')' || character == ']' || character == '}') {
                val opening = when (character) {
                    ')' -> '('
                    ']' -> '['
                    else -> '{'
                }
                val candidate = value.substring(0, length)
                if (candidate.count { it == character } > candidate.count { it == opening }) {
                    length--
                    continue
                }
            }

            break
        }
        return value.substring(0, length)
    }
}
