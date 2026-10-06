package org.unishare.app

import java.nio.charset.StandardCharsets
import java.util.Locale

data class InspectedFileContent(val mimeType: String)

/**
 * Resolves the stored MIME type from bounded file bytes instead of trusting a content provider.
 * Active web documents are not accepted as ordinary assets: offline pages must pass through the
 * sanitising capture flow.
 */
object FileContentPolicy {
    const val MAXIMUM_HEADER_BYTES = 512

    private val activeMimeTypes = setOf(
        "text/html",
        "application/xhtml+xml",
        "image/svg+xml",
    )
    private val activeExtensions = setOf("html", "htm", "xhtml", "svg")
    private val signatureMimeTypes = setOf(
        "application/pdf",
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "application/zip",
        "audio/mpeg",
        "audio/mp4",
        "image/avif",
        "image/heif",
        "video/mp4",
        "video/3gpp",
        "video/3gpp2",
        "video/quicktime",
    )
    private val zipContainerMimeTypes = setOf(
        "application/epub+zip",
        "application/java-archive",
        "application/vnd.android.package-archive",
        "application/vnd.oasis.opendocument.presentation",
        "application/vnd.oasis.opendocument.spreadsheet",
        "application/vnd.oasis.opendocument.text",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    )

    fun inspect(header: ByteArray, declaredMimeType: String?, fileName: String): InspectedFileContent {
        val detected = detect(header)
        val declared = normalizeMimeType(declaredMimeType)
        val extension = fileName.substringAfterLast('.', "").lowercase(Locale.ROOT)
        val extensionMime = mimeFromExtension(extension)

        if (detected in activeMimeTypes || declared in activeMimeTypes || extension in activeExtensions) {
            throw IllegalArgumentException(
                "No se importa HTML o SVG activo como archivo. Guarda el enlace o crea una copia offline segura.",
            )
        }

        if (detected != null && declared != null && declared in signatureMimeTypes &&
            !equivalent(detected, declared)
        ) {
            throw IllegalArgumentException(
                "El contenido del archivo no coincide con el tipo declarado por la aplicación de origen.",
            )
        }
        if (detected == null && declared in signatureMimeTypes) {
            throw IllegalArgumentException(
                "El archivo no contiene la firma esperada para el tipo declarado.",
            )
        }

        val resolved = when {
            detected == "application/zip" && declared != null && declared in zipContainerMimeTypes -> declared
            detected == "application/zip" && extensionMime != null && extensionMime in zipContainerMimeTypes ->
                extensionMime
            detected != null -> detected
            declared != null -> declared
            else -> extensionMime ?: "application/octet-stream"
        }
        return InspectedFileContent(resolved)
    }

    internal fun detect(content: ByteArray): String? {
        if (content.startsWithAscii("%PDF-")) return "application/pdf"
        if (content.startsWithBytes(0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a)) return "image/png"
        if (content.startsWithBytes(0xff, 0xd8, 0xff)) return "image/jpeg"
        if (content.startsWithAscii("GIF87a") || content.startsWithAscii("GIF89a")) return "image/gif"
        if (content.size >= 12 && content.asciiAt(0, 4) == "RIFF" && content.asciiAt(8, 4) == "WEBP") {
            return "image/webp"
        }
        if (content.startsWithBytes(0x50, 0x4b, 0x03, 0x04) ||
            content.startsWithBytes(0x50, 0x4b, 0x05, 0x06) ||
            content.startsWithBytes(0x50, 0x4b, 0x07, 0x08)
        ) {
            return "application/zip"
        }
        if (content.startsWithAscii("ID3") ||
            (content.size >= 2 && content[0].unsigned() == 0xff && content[1].unsigned() and 0xe0 == 0xe0)
        ) {
            return "audio/mpeg"
        }
        if (content.size >= 12 && content.asciiAt(4, 4) == "ftyp") {
            return when (val brand = content.asciiAt(8, 4).lowercase(Locale.ROOT)) {
                "avif", "avis" -> "image/avif"
                "heic", "heix", "hevc", "hevx", "heim", "heis", "hevm", "hevs", "mif1", "msf1" ->
                    "image/heif"
                "m4a " -> "audio/mp4"
                "qt  " -> "video/quicktime"
                else -> when {
                    brand.startsWith("3gp") -> "video/3gpp"
                    brand.startsWith("3g2") -> "video/3gpp2"
                    else -> "video/mp4"
                }
            }
        }

        if (content.isEmpty() || content.any { it == 0.toByte() }) return null
        val text = String(content, StandardCharsets.UTF_8)
            .trimStart('\uFEFF', ' ', '\t', '\r', '\n')
        if (text.startsWith("<!doctype html", ignoreCase = true) ||
            text.startsWith("<html", ignoreCase = true)
        ) {
            return "text/html"
        }
        if (text.startsWith("<svg", ignoreCase = true) ||
            (text.startsWith("<?xml", ignoreCase = true) && text.contains("<svg", ignoreCase = true))
        ) {
            return "image/svg+xml"
        }
        return null
    }

    private fun normalizeMimeType(value: String?): String? {
        val normalized = value?.substringBefore(';')?.trim()?.lowercase(Locale.ROOT)
            ?.takeIf { it.isNotBlank() && it.length <= 200 }
            ?: return null
        return when (normalized) {
            "application/x-zip-compressed" -> "application/zip"
            "audio/mp3" -> "audio/mpeg"
            "image/heic", "image/heic-sequence", "image/heif-sequence" -> "image/heif"
            "image/jpg", "image/pjpeg" -> "image/jpeg"
            else -> normalized
        }
    }

    private fun equivalent(detected: String, declared: String): Boolean = detected == declared

    private fun mimeFromExtension(extension: String): String? = when (extension) {
        "txt", "md" -> "text/plain"
        "csv" -> "text/csv"
        "json" -> "application/json"
        "xml" -> "application/xml"
        "pdf" -> "application/pdf"
        "png" -> "image/png"
        "jpg", "jpeg" -> "image/jpeg"
        "gif" -> "image/gif"
        "webp" -> "image/webp"
        "zip" -> "application/zip"
        "apk" -> "application/vnd.android.package-archive"
        "docx" -> "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        "xlsx" -> "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        "pptx" -> "application/vnd.openxmlformats-officedocument.presentationml.presentation"
        "odt" -> "application/vnd.oasis.opendocument.text"
        "ods" -> "application/vnd.oasis.opendocument.spreadsheet"
        "odp" -> "application/vnd.oasis.opendocument.presentation"
        "epub" -> "application/epub+zip"
        "jar" -> "application/java-archive"
        "mp3" -> "audio/mpeg"
        "m4a" -> "audio/mp4"
        "mp4", "m4v" -> "video/mp4"
        "mov" -> "video/quicktime"
        "3gp" -> "video/3gpp"
        "3g2" -> "video/3gpp2"
        "heic", "heif" -> "image/heif"
        "avif" -> "image/avif"
        else -> null
    }

    private fun ByteArray.startsWithAscii(value: String): Boolean =
        startsWith(value.toByteArray(StandardCharsets.US_ASCII))

    private fun ByteArray.startsWithBytes(vararg values: Int): Boolean {
        if (size < values.size) return false
        return values.indices.all { this[it].unsigned() == values[it] }
    }

    private fun ByteArray.startsWith(prefix: ByteArray): Boolean {
        if (size < prefix.size) return false
        return prefix.indices.all { this[it] == prefix[it] }
    }

    private fun ByteArray.asciiAt(offset: Int, length: Int): String =
        String(this, offset, length, StandardCharsets.US_ASCII)

    private fun Byte.unsigned(): Int = toInt() and 0xff
}
