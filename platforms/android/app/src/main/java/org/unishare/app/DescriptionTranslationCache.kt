package org.unishare.app

import org.json.JSONObject
import java.io.File
import java.security.MessageDigest
import java.util.UUID
import org.unishare.app.TranslationModelCache.Companion.hex

internal data class DescriptionTranslationResult(val status: String, val text: String, val source: String, val target: String) {
    val isTranslated: Boolean get() = status == "translated"
}

/** Discardable reading views, never part of domain tables or synchronization. */
internal class DescriptionTranslationCache(private val root: File) {
    fun read(text: String, target: String): DescriptionTranslationResult? = runCatching {
        val file = File(root, key(text, target) + ".json")
        if (!file.isFile || file.length() > 1_600_000) return null
        val json = JSONObject(file.readText())
        DescriptionTranslationResult(json.getString("status"), json.getString("text"), json.getString("source"), json.getString("target"))
            .takeIf { valid(it, target) }
    }.getOrNull()

    fun write(text: String, result: DescriptionTranslationResult) {
        if (!valid(result, result.target)) return
        val target = File(root, key(text, result.target) + ".json")
        check(root.isDirectory || root.mkdirs())
        val partial = File(root, "${target.name}.${UUID.randomUUID()}.partial")
        try {
            partial.writeText(JSONObject().put("status", result.status).put("text", result.text)
                .put("source", result.source).put("target", result.target).toString())
            java.nio.file.Files.move(partial.toPath(), target.toPath(), java.nio.file.StandardCopyOption.REPLACE_EXISTING,
                java.nio.file.StandardCopyOption.ATOMIC_MOVE)
        } finally { partial.delete() }
    }

    companion object {
        const val ENGINE_VERSION = "bergamot4-models3-policy1"
        fun key(text: String, target: String): String {
            require(target in setOf("en", "es") && text.length <= 100_000)
            return MessageDigest.getInstance("SHA-256").digest("$ENGINE_VERSION\u0000$target\u0000$text".toByteArray(Charsets.UTF_8)).hex()
        }
        private fun valid(result: DescriptionTranslationResult, target: String): Boolean =
            result.status in setOf("translated", "original", "unsupported") && result.target == target &&
                result.text.length <= 400_000 && result.source.length <= 16
    }
}
