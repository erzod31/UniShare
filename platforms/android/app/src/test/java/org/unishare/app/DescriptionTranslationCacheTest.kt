package org.unishare.app

import java.nio.file.Files
import org.junit.Assert.*
import org.junit.Test

class DescriptionTranslationCacheTest {
    @Test
    fun restartKeepsTheViewButAnEditOrDifferentLanguageDoesNotReuseIt() {
        val root = Files.createTempDirectory("unishare-translation-").toFile()
        try {
            val original = "Hello 🌍\nhttps://example.com/?a=1&b=2"
            val result = DescriptionTranslationResult("translated", "Hola 🌍\nhttps://example.com/?a=1&b=2", "en", "es")
            DescriptionTranslationCache(root).write(original, result)
            val restarted = DescriptionTranslationCache(root)
            assertEquals(result, restarted.read(original, "es"))
            assertNull(restarted.read(original + " edited", "es"))
            assertNull(restarted.read(original, "en"))
            assertEquals(1, root.listFiles()!!.size)
        } finally { root.deleteRecursively() }
    }

    @Test
    fun temporaryFailureDoesNotPoisonRetriesAndDamagedCacheCanBeReplaced() {
        val root = Files.createTempDirectory("unishare-translation-").toFile()
        try {
            val cache = DescriptionTranslationCache(root)
            cache.write("Hello", DescriptionTranslationResult("unavailable", "Hello", "", "es"))
            assertNull(cache.read("Hello", "es"))
            assertEquals(0, root.listFiles()!!.size)
            java.io.File(root, DescriptionTranslationCache.key("Hello", "es") + ".json").writeText("{broken")
            assertNull(cache.read("Hello", "es"))
            cache.write("Hello", DescriptionTranslationResult("translated", "Hola", "en", "es"))
            assertEquals("Hola", cache.read("Hello", "es")!!.text)
        } finally { root.deleteRecursively() }
    }

    @Test
    fun targetsCannotChooseAPathAndUnicodeIsPartOfTheCacheIdentity() {
        assertThrows(IllegalArgumentException::class.java) { DescriptionTranslationCache.key("Hello", "../secret") }
        assertTrue(Regex("[a-f0-9]{64}").matches(DescriptionTranslationCache.key("../private 🌍", "es")))
        assertNotEquals(DescriptionTranslationCache.key("Hello 🌍", "es"), DescriptionTranslationCache.key("Hello 🌎", "es"))
    }
}
