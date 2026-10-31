package org.unishare.app

import android.os.SystemClock
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File

/** Explicit model-network test; run separately from the core device suite. */
@RunWith(AndroidJUnit4::class)
class LocalTranslationRuntimeTest {
    @Test
    fun realLocalEngineTranslatesBothDirectionsAndPivotWithoutChangingStoredDescriptions() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val context = instrumentation.targetContext
        check(context.packageName.endsWith(".qa"))
        DescriptionTranslationSettings.save(context, false)
        File(context.cacheDir, "translation/results").listFiles()?.filter { it.name.endsWith(".json") }?.forEach { it.delete() }
        val cases = listOf(
            Triple("This is a practical guide to organizing your personal library. You can save links and files without an internet connection. Visit https://example.com/guide?a=1&b=2 🌍 #Obsidian", "es", "biblioteca"),
            Triple("Esta es una guía práctica para organizar tu biblioteca personal. Puedes guardar enlaces y archivos sin conexión a Internet. Visita https://example.com/guide?a=1&b=2 🌍 #Obsidian", "en", "library"),
            Triple("Ce guide explique comment organiser votre bibliothèque personnelle. Vous pouvez enregistrer des liens et des fichiers sans connexion Internet.", "es", "biblioteca"),
        )
        val store = (context.applicationContext as UniShareApplication).store
        val saved = store.saveLink("Local translator QA ${SystemClock.elapsedRealtime()}", "https://example.invalid/local-translation", "QA")
        store.updateItem(saved.id, saved.title, null, null, cases[0].first, false, "", "QA")
        val results = mutableListOf<DescriptionTranslationResult>()
        ActivityScenario.launch(MainActivity::class.java).use { scenario ->
          for (offline in listOf(false, true)) {
            val instance = CompletableDeferred<LocalDescriptionTranslator>()
            scenario.onActivity { activity ->
                val offlineRoot = File(context.cacheDir, "translation-offline-${SystemClock.elapsedRealtime()}")
                val local = if (offline) {
                    val catalog = context.assets.open("translation/models.json").bufferedReader().use { it.readText() }
                    File(context.cacheDir, "translation/models").copyRecursively(File(offlineRoot, "models"), overwrite = false)
                    LocalDescriptionTranslator(activity, offlineRoot,
                        TranslationModelCache(catalog, File(offlineRoot, "models")) { throw java.io.IOException("Network deliberately blocked") })
                } else LocalDescriptionTranslator(activity)
                local.view.webChromeClient = object : android.webkit.WebChromeClient() {
                    override fun onConsoleMessage(message: android.webkit.ConsoleMessage): Boolean {
                        println("Translation QA JS: ${message.sourceId()}:${message.lineNumber()} ${message.message()}")
                        return true
                    }
                }
                activity.addContentView(local.view, android.view.ViewGroup.LayoutParams(1, 1))
                instance.complete(local)
            }
            val translator = runBlocking { instance.await() }
            try {
                for ((text, target, word) in cases) {
                    val result = runBlocking { translator.translate(text, target) }
                    assertEquals("Expected real local inference (offline=$offline)", "translated", result.status)
                    assertEquals(target, result.target)
                    assertTrue(result.text.contains(word, ignoreCase = true))
                    if (text.contains("https://")) assertTrue(result.text.contains("https://example.com/guide?a=1&b=2 🌍 #Obsidian"))
                    results += result
                }
                val same = runBlocking { translator.translate(cases[0].first, "en") }
                assertEquals("original", same.status)
                assertEquals(cases[0].first, same.text)
                assertEquals(cases[0].first, store.listItems(query = saved.title).single().description)
                File(context.getExternalFilesDir(null), "local-translation-results.json").writeText(
                    org.json.JSONArray(results.map { r -> org.json.JSONObject().put("status",r.status)
                        .put("source",r.source).put("target",r.target).put("text",r.text) }).toString(2))
            } finally { scenario.onActivity {
                (translator.view.parent as? android.view.ViewGroup)?.removeView(translator.view)
                translator.close()
            } }
          }
        }
    }

    @Test
    fun automaticReadingViewUsesTheSettingsLanguageAndOffersTheUnchangedOriginal() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val context = instrumentation.targetContext
        check(context.packageName.endsWith(".qa"))
        val device = androidx.test.uiautomator.UiDevice.getInstance(instrumentation)
        val text = "Ce guide explique comment organiser votre bibliothèque personnelle. Vous pouvez enregistrer des liens et des fichiers sans connexion Internet."
        val store = (context.applicationContext as UniShareApplication).store
        val title = "Translation view QA ${SystemClock.elapsedRealtime()}"
        val item = store.saveLink(title, "https://example.invalid/translation-view", "QA")
        store.updateItem(item.id, title, null, null, text, false, "", "QA")
        UiLanguageSettings.save(context, UiLanguageChoice.SPANISH)
        DescriptionTranslationSettings.save(context, true)
        val cache = DescriptionTranslationCache(File(context.cacheDir, "translation/results"))
        fun waitForText(value: String): androidx.test.uiautomator.UiObject2 {
            device.wait(androidx.test.uiautomator.Until.findObject(androidx.test.uiautomator.By.text(value)), 10_000)?.let { return it }
            repeat(8) {
                device.findObject(androidx.test.uiautomator.By.scrollable(true))?.scroll(androidx.test.uiautomator.Direction.DOWN, 0.4f)
                device.wait(androidx.test.uiautomator.Until.findObject(androidx.test.uiautomator.By.text(value)), 1000)?.let { return it }
            }
            error("Missing translation UI control: $value")
        }
        fun translated(target: String): String {
            val deadline = SystemClock.elapsedRealtime() + 180_000
            while (SystemClock.elapsedRealtime() < deadline) {
                cache.read(text, target)?.takeIf { it.isTranslated }?.let { return it.text }
                Thread.sleep(100)
            }
            error("The automatic reading view did not produce $target")
        }
        fun navigation(label: String) = checkNotNull(device.wait(androidx.test.uiautomator.Until.findObject(
            androidx.test.uiautomator.By.desc(label)), 10_000)) { "Missing navigation destination: $label" }
        try {
            ActivityScenario.launch(MainActivity::class.java).use { scenario ->
                waitForText(title).click()
                waitForText(translated("es"))
                waitForText("Ver original").click()
                waitForText(text)
                waitForText("Cancelar").click()
                navigation("Ajustes").click()
                waitForText("Idioma: Español").click()
                navigation("Library").click()
                waitForText(title).click()
                waitForText(translated("en"))
                waitForText("Show original").click()
                waitForText(text)
                waitForText("Edit summary").click()
                waitForText(text)
                assertEquals(text, store.listItems(query = title).single().description)
                scenario.recreate()
                assertEquals(UiLanguageChoice.ENGLISH, UiLanguageSettings.load(context))
                assertTrue(DescriptionTranslationSettings.load(context))
            }
        } finally {
            DescriptionTranslationSettings.save(context, false)
            UiLanguageSettings.save(context, UiLanguageChoice.SPANISH)
        }
    }
}
