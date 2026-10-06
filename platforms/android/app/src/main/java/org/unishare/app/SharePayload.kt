package org.unishare.app

import java.net.URI
import java.net.IDN

data class SharedLink(val title: String, val url: String)

object SharePayload {
    fun parse(text: String?): SharedLink? {
        val raw = text?.trim().orEmpty()
        if (raw.isEmpty()) return null
        if (isWebUrl(raw)) return SharedLink(titleFromUrl(raw), raw)

        val candidate = raw.split(Regex("\\s+"))
            .firstOrNull(::isWebUrl)
            ?: return null
        val title = raw.substringBefore(candidate).trim().take(500).ifEmpty { titleFromUrl(candidate) }
        return SharedLink(title, candidate)
    }

    fun isWebUrl(value: String): Boolean = try {
        val uri = URI(value.trim())
        (uri.scheme.equals("http", true) || uri.scheme.equals("https", true)) &&
            !uri.host.isNullOrBlank() && uri.userInfo == null
    } catch (_: Exception) {
        false
    }

    fun titleFromUrl(value: String): String {
        require(isWebUrl(value)) { "Usa un enlace http o https válido" }
        val host = URI(value.trim()).host.removePrefix("www.")
        return host.take(500)
    }

    fun canonicalize(value: String): String {
        require(isWebUrl(value)) { "Usa un enlace http o https válido" }
        val uri = URI(value.trim())
        val scheme = uri.scheme.lowercase()
        val host = IDN.toASCII(uri.host).lowercase()
        val port = if ((scheme == "http" && uri.port == 80) || (scheme == "https" && uri.port == 443)) {
            -1
        } else {
            uri.port
        }
        val path = uri.rawPath?.ifEmpty { "/" } ?: "/"
        val query = uri.rawQuery?.split('&')?.filter { it.isNotEmpty() && !isTrackingParameter(it) }
            ?.joinToString("&")?.ifEmpty { null }
        return URI(scheme, uri.userInfo, host, port, path, query, null).toASCIIString()
    }

    private fun isTrackingParameter(pair: String): Boolean {
        @Suppress("DEPRECATION")
        val name = java.net.URLDecoder.decode(pair.substringBefore('=').replace("+", " "), "UTF-8")
        return name.startsWith("utm_", true) || name.equals("fbclid", true) ||
            name.equals("gclid", true) || name.equals("mc_cid", true) || name.equals("mc_eid", true)
    }
}
