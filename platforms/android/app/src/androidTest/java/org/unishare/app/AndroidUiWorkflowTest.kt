package org.unishare.app

import android.content.Intent
import android.os.Build
import androidx.test.core.app.ActivityScenario
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import androidx.test.uiautomator.By
import androidx.test.uiautomator.BySelector
import androidx.test.uiautomator.Direction
import androidx.test.uiautomator.UiDevice
import androidx.test.uiautomator.Until
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import java.util.UUID

@RunWith(AndroidJUnit4::class)
class AndroidUiWorkflowTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context = instrumentation.targetContext
    private val device = UiDevice.getInstance(instrumentation)

    @Before
    fun requireIsolatedQaApplication() {
        check(context.packageName.endsWith(".qa")) { "UI workflows must use the isolated QA application." }
        UiLanguageSettings.save(context, UiLanguageChoice.SPANISH)
    }

    @Test
    fun unsavedLinkAndFolderSurviveActivityRecreation() {
        val link = "https://example.invalid/draft-${UUID.randomUUID()}"
        ActivityScenario.launch(MainActivity::class.java).use { scenario ->
            node(By.desc("Guardar un elemento")).click()
            input("Enlace http/https", link)
            input("Carpeta (opcional)", "Borradores")
            node(By.text("Copia offline")).click()

            scenario.recreate()

            node(By.text("Guardar en UniShare"))
            node(By.text(link))
            node(By.text("Borradores"))
            val choice = node(By.text("Copia offline")).parent
            assertTrue(choice.isSelected || choice.isChecked)
        }
    }

    @Test
    fun saveSheetCanImportASystemDocumentWithoutLosingItsSource() {
        assumeTrue(Build.VERSION.SDK_INT >= 29)
        val fileName = "unishare-ui-${UUID.randomUUID()}.txt"
        val bytes = "Archivo elegido con el selector Android".toByteArray()
        val resolver = context.contentResolver
        val document = checkNotNull(resolver.insert(android.provider.MediaStore.Downloads.EXTERNAL_CONTENT_URI,
            android.content.ContentValues().apply {
                put(android.provider.MediaStore.MediaColumns.DISPLAY_NAME, fileName)
                put(android.provider.MediaStore.MediaColumns.MIME_TYPE, "text/plain")
                put(android.provider.MediaStore.MediaColumns.IS_PENDING, 1)
            }))
        checkNotNull(resolver.openOutputStream(document)).use { it.write(bytes) }
        resolver.update(document, android.content.ContentValues().apply {
            put(android.provider.MediaStore.MediaColumns.IS_PENDING, 0)
        }, null, null)
        try {
          ActivityScenario.launch(MainActivity::class.java).use { scenario ->
            node(By.desc("Guardar un elemento")).click()
            input("Enlace http/https", "https://example.invalid/saf")
            input("Carpeta (opcional)", "Documentos QA")
            node(By.text("Elegir archivo…")).click()
            scenario.onActivity { it.recreate() }
            val selected = device.wait(Until.findObject(By.text(fileName)), 10_000)
                ?: run {
                    val menu = device.findObject(By.desc("Show roots"))
                        ?: device.findObject(By.desc("Mostrar raíces"))
                        ?: device.findObject(By.desc("Afficher les racines"))
                    menu?.click()
                    val downloads = device.wait(Until.findObject(By.text("Downloads")), 5_000)
                        ?: device.findObject(By.text("Descargas"))
                        ?: device.findObject(By.text("Téléchargements"))
                    downloads?.click()
                    device.wait(Until.findObject(By.text(fileName)), 10_000)
                }
            checkNotNull(selected) { "The system document picker did not expose the QA fixture." }.click()
            val store = (context.applicationContext as UniShareApplication).store
            val deadline = System.currentTimeMillis() + 15_000
            while (store.listItems(query = fileName).isEmpty() && System.currentTimeMillis() < deadline) Thread.sleep(100)
            val imported = store.listItems(query = fileName).single()
            assertEquals(3, imported.kind)
            assertEquals("https://example.invalid/saf", imported.originalUrl)
            assertEquals(listOf("Documentos QA"), imported.collections)
            resolver.delete(document, null, null)
            assertArrayEquals(bytes, store.localAsset(imported.id)!!.file.readBytes())
          }
        } finally {
            resolver.delete(document, null, null)
        }
    }

    @Test
    fun automaticTranslationPreferencePersistsAndEditingAlwaysUsesTheOriginal() {
        val store = (context.applicationContext as UniShareApplication).store
        val original = "Hello world 🌍 https://example.com/guide?a=1&b=2"
        for (language in listOf(UiLanguageChoice.SPANISH, UiLanguageChoice.ENGLISH)) {
            UiLanguageSettings.save(context, language)
            // This UI test exercises the preference and editing; real inference has its own test.
            DescriptionTranslationSettings.save(context, false)
            val title = "Translation QA ${UUID.randomUUID()}"
            val item = store.saveLink(title, "https://example.invalid/translation-${UUID.randomUUID()}", "QA")
            store.updateItem(item.id, title, null, null, original, false, "", "QA")
            val observed = java.util.concurrent.atomic.AtomicReference<Intent?>()
            val monitor = object : android.app.Instrumentation.ActivityMonitor() {
                override fun onStartActivity(intent: Intent): android.app.Instrumentation.ActivityResult? {
                    if (intent.action == Intent.ACTION_VIEW && intent.data?.host == "translate.google.com") {
                        observed.set(intent)
                        // Test the real launch action without sending even synthetic text to the provider.
                        return android.app.Instrumentation.ActivityResult(android.app.Activity.RESULT_CANCELED, Intent())
                    }
                    return null
                }
            }
            instrumentation.addMonitor(monitor)
            try {
                ActivityScenario.launch(MainActivity::class.java).use {
                    node(By.text(title)).click()
                    node(By.text(original))
                    node(By.text(if (language == UiLanguageChoice.ENGLISH) "Edit summary" else "Modificar resumen")).click()
                    node(By.text(original))
                    assertNull(observed.get())
                    node(By.text(if (language == UiLanguageChoice.ENGLISH) "Cancel" else "Cancelar")).click()
                    assertNull(observed.get())
                    node(By.desc(if (language == UiLanguageChoice.ENGLISH) "Settings" else "Ajustes")).click()
                    val preference = node(By.desc(if (language == UiLanguageChoice.ENGLISH) "Translate descriptions automatically" else "Traducir descripciones automáticamente"))
                    preference.click()
                    assertTrue(DescriptionTranslationSettings.load(context))
                    it.recreate()
                    assertTrue(DescriptionTranslationSettings.load(context))
                    node(By.desc(if (language == UiLanguageChoice.ENGLISH) "Translate descriptions automatically" else "Traducir descripciones automáticamente")).click()
                    assertFalse(DescriptionTranslationSettings.load(context))
                    assertNull(observed.get())
                    assertEquals(original, store.listItems(query = title).single().description)
                }
            } finally {
                instrumentation.removeMonitor(monitor)
            }
        }
        UiLanguageSettings.save(context, UiLanguageChoice.SPANISH)
    }

    private fun node(selector: BySelector): androidx.test.uiautomator.UiObject2 {
        device.wait(Until.findObject(selector), 5_000)?.let { return it }
        repeat(5) {
            val pane = device.findObject(By.scrollable(true)) ?: return@repeat
            pane.scroll(Direction.DOWN, 0.65f)
            device.wait(Until.findObject(selector), 1_000)?.let { return it }
        }
        val evidence = context.getExternalFilesDir(null)
        device.dumpWindowHierarchy(java.io.File(evidence, "ui-failure.xml"))
        device.takeScreenshot(java.io.File(evidence, "ui-failure.png"))
        error("The expected UI control is missing: $selector")
    }

    private fun input(label: String, value: String) {
        node(By.clazz("android.widget.EditText").hasDescendant(By.text(label))).text = value
    }
}

@RunWith(AndroidJUnit4::class)
class AndroidShareAndPermissionTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()
    private val context = instrumentation.targetContext

    @Before
    fun requireIsolatedQaApplication() {
        check(context.packageName.endsWith(".qa"))
        UiLanguageSettings.save(context, UiLanguageChoice.SPANISH)
    }

    @Test
    fun systemBarIconsFollowLightAndDarkThemes() {
        val original = ThemeSettings.load(context)
        try {
            for (theme in listOf(ThemePreference.LIGHT, ThemePreference.DARK)) {
                ThemeSettings.save(context, theme)
                ActivityScenario.launch(MainActivity::class.java).use { scenario ->
                    instrumentation.waitForIdleSync()
                    scenario.onActivity { activity ->
                        val controller = androidx.core.view.WindowCompat.getInsetsController(activity.window, activity.window.decorView)
                        assertEquals(theme == ThemePreference.LIGHT, controller.isAppearanceLightStatusBars)
                        assertEquals(theme == ThemePreference.LIGHT, controller.isAppearanceLightNavigationBars)
                    }
                }
            }
        } finally {
            ThemeSettings.save(context, original)
        }
    }

    @Test
    fun sharedLinkIsSavedOnceAndDoesNotRepeatOnRecreation() {
        val link = "https://example.invalid/shared-${UUID.randomUUID()}"
        val store = (context.applicationContext as UniShareApplication).store
        val intent = Intent(context, MainActivity::class.java).apply {
            action = Intent.ACTION_SEND
            type = "text/plain"
            putExtra(Intent.EXTRA_TEXT, "Compartido QA $link")
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        }
        context.startActivity(intent)
        try {
            val deadline = System.currentTimeMillis() + 10_000
            while (store.listItems(query = link).isEmpty() && System.currentTimeMillis() < deadline) {
                Thread.sleep(100)
            }
            assertEquals(1, store.listItems(query = link).size)
            instrumentation.runOnMainSync {
                androidx.test.runner.lifecycle.ActivityLifecycleMonitorRegistry.getInstance()
                    .getActivitiesInStage(androidx.test.runner.lifecycle.Stage.RESUMED)
                    .filterIsInstance<MainActivity>().single().recreate()
            }
            instrumentation.waitForIdleSync()
            assertEquals(1, store.listItems(query = link).size)
        } finally {
            instrumentation.runOnMainSync {
                androidx.test.runner.lifecycle.ActivityLifecycleMonitorRegistry.getInstance()
                    .getActivitiesInStage(androidx.test.runner.lifecycle.Stage.RESUMED)
                    .filterIsInstance<MainActivity>().forEach { it.finish() }
            }
        }
    }

    @Test
    fun android17ShowsTheRuntimePromptAndDenialKeepsAnHonestPendingState() {
        assumeTrue(Build.VERSION.SDK_INT >= 37)
        shell("pm clear-permission-flags ${context.packageName} ${LocalNetworkAccess.PERMISSION} user-set user-fixed")
        val settings = DirectSyncPreferences.save(context, "https://computer.example.ts.net", "0".repeat(64))
        try {
            assertFalse(LocalNetworkAccess.isGranted(context))
            ActivityScenario.launch(MainActivity::class.java).use {
                val device = UiDevice.getInstance(instrumentation)
                val deny = device.wait(Until.findObject(By.res(java.util.regex.Pattern.compile(".*:id/permission_deny_button"))), 10_000)
                checkNotNull(deny) { "Android 17 did not show its local-network permission prompt." }.click()
                val deadline = System.currentTimeMillis() + 10_000
                while (DirectSyncPreferences.lastResult(context)?.contains("Sin permiso de red local") != true &&
                    System.currentTimeMillis() < deadline) Thread.sleep(100)
                assertTrue(DirectSyncPreferences.lastResult(context).orEmpty().contains("pendiente:"))
                assertTrue(DirectSyncPreferences.lastResult(context).orEmpty().contains("Sin permiso de red local"))
                assertFalse(LocalNetworkAccess.isGranted(context))
            }
            ActivityScenario.launch(MainActivity::class.java).use {
                val device = UiDevice.getInstance(instrumentation)
                val allow = device.wait(Until.findObject(By.res(java.util.regex.Pattern.compile(".*:id/permission_allow_button"))), 10_000)
                checkNotNull(allow) { "Android 17 did not allow the permission to be requested again." }.click()
                val deadline = System.currentTimeMillis() + 10_000
                while (!LocalNetworkAccess.isGranted(context) && System.currentTimeMillis() < deadline) Thread.sleep(100)
                assertTrue(LocalNetworkAccess.isGranted(context))
            }
        } finally {
            // Remove only this QA pairing so following workflows never contact a real peer.
            context.getSharedPreferences("direct_sync", 0).edit().clear().commit()
            assertEquals("https://computer.example.ts.net", settings.endpoint)
            shell("pm grant ${context.packageName} ${LocalNetworkAccess.PERMISSION}")
        }
    }

    private fun shell(command: String) {
        instrumentation.uiAutomation.executeShellCommand(command).use { descriptor ->
            android.os.ParcelFileDescriptor.AutoCloseInputStream(descriptor).use { it.readBytes() }
        }
    }
}
