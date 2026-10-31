package org.unishare.app

import java.io.ByteArrayInputStream
import java.io.File
import java.nio.file.Files
import java.security.MessageDigest
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import org.unishare.app.TranslationModelCache.Companion.hex

class TranslationModelCacheTest {
    private val bytes = "a synthetic immutable model 🌍".toByteArray()
    private val hash get() = MessageDigest.getInstance("SHA-256").digest(bytes).hex()
    private fun catalog(url: String = "https://firefox-settings-attachments.cdn.mozilla.net/model.zst"): String =
        JSONObject().put("pairs", JSONObject().put("en-es", JSONObject().put("files", JSONObject().put("model", JSONObject()
            .put("sha256", hash).put("size", bytes.size).put("url", url)
            .put("decompressedSha256", "b".repeat(64)).put("decompressedSize", 1024))))).toString()

    @Test
    fun verifiedModelsSurviveRestartWithoutNetwork() {
        val root = Files.createTempDirectory("unishare-model-").toFile()
        try {
            TranslationModelCache(catalog(), root) { ByteArrayInputStream(bytes) }.use { assertArrayEquals(bytes, it.get(hash).readBytes()) }
            TranslationModelCache(catalog(), root) { error("Network intentionally unavailable") }.use { assertArrayEquals(bytes, it.get(hash).readBytes()) }
            assertEquals(1, root.listFiles()!!.size)
        } finally { root.deleteRecursively() }
    }

    @Test
    fun incompleteAndWrongHashTransfersNeverBecomeAvailable() {
        val root = Files.createTempDirectory("unishare-model-").toFile()
        try {
            for (invalid in listOf(bytes.copyOf(bytes.size - 1), ByteArray(bytes.size))) {
                TranslationModelCache(catalog(), root) { ByteArrayInputStream(invalid) }.use { cache ->
                    assertThrows(IllegalArgumentException::class.java) { cache.get(hash) }
                }
                assertEquals(0, root.listFiles()!!.size)
            }
        } finally { root.deleteRecursively() }
    }

    @Test
    fun corruptionIsRepairedAndUnknownHashesAreRejectedBeforeNetwork() {
        val root = Files.createTempDirectory("unishare-model-").toFile()
        try {
            File(root, hash).writeBytes(ByteArray(bytes.size))
            TranslationModelCache(catalog(), root) { ByteArrayInputStream(bytes) }.use { cache ->
                assertArrayEquals(bytes, cache.get(hash).readBytes())
                assertThrows(IllegalArgumentException::class.java) { cache.get("../other") }
                assertThrows(IllegalArgumentException::class.java) { cache.get("a".repeat(64)) }
            }
        } finally { root.deleteRecursively() }
    }

    @Test
    fun onlyTheImmutablePublicModelHostIsAccepted() {
        val root = Files.createTempDirectory("unishare-model-").toFile()
        try {
            for (url in listOf("https://example.com/model", "http://firefox-settings-attachments.cdn.mozilla.net/model",
                "https://key@firefox-settings-attachments.cdn.mozilla.net/model")) {
                assertThrows(IllegalArgumentException::class.java) { TranslationModelCache(catalog(url), root) }
            }
        } finally { root.deleteRecursively() }
    }
}
