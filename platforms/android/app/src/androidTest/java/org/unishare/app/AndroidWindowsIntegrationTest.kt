package org.unishare.app

import android.net.Uri
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.util.UUID

/** Run separately with a disposable Windows node and adb reverse; never uses the user's pairing. */
@RunWith(AndroidJUnit4::class)
class AndroidWindowsIntegrationTest {
    @Test
    fun actualWindowsNodeAndAndroidExchangeMetadataFilesAndAcknowledgements() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val base = instrumentation.targetContext
        check(base.packageName.endsWith(".qa"))
        val arguments = InstrumentationRegistry.getArguments()
        val endpoint = checkNotNull(arguments.getString("windowsEndpoint"))
        val key = checkNotNull(arguments.getString("windowsKey"))
        val seedTitle = checkNotNull(arguments.getString("windowsSeedTitle"))
        require(endpoint.startsWith("http://127.0.0.1:"))
        IsolatedLibraryContext(base).use { context ->
            LocalLibraryStore(context).use { store ->
                val settings = DirectSyncPreferences.save(context, endpoint, key)
                val item = store.saveLink("Android QA", "https://example.invalid/phone-${UUID.randomUUID()}", "QA")
                store.updateItem(item.id, "Android QA", "Teléfono", "Autora QA", "Resumen conservado", true, "Pruebas", "QA")
                val bytes = ByteArray(131_072).also { java.util.Random(47).nextBytes(it) }
                AndroidPersistenceTest.attach(store, context, item.id, bytes)

                val first = store.synchronizeDirect(settings)
                assertEquals(0, first.conflicts)
                assertEquals(seedTitle, store.listItems(query = seedTitle).single().title)
                val fromWindows = store.listItems(query = seedTitle).single()
                assertEquals(3, fromWindows.kind)
                val capturedFile = checkNotNull(store.localAsset(fromWindows.id)).file
                val captured = capturedFile.readBytes()
                assertTrue(String(captured).contains("Contenido del nodo Windows"))
                assertFalse(store.hasPendingDirectOperations())

                store.updateItem(item.id, "Android QA editado", "Teléfono", "Autora QA", "Resumen editado", true,
                    "Pruebas", "QA")
                assertEquals(0, store.synchronizeDirect(settings).conflicts)
                assertFalse(store.hasPendingDirectOperations())
                assertArrayEquals(bytes, store.localAsset(item.id)!!.file.readBytes())
                assertTrue(store.listSyncConflicts().isEmpty())
                // Metadata is unchanged: inventory negotiation must still retransmit this blob.
                capturedFile.writeBytes(ByteArray(captured.size))
                store.synchronizeDirect(settings)
                assertArrayEquals(captured, capturedFile.readBytes())
                val exported = File(base.getExternalFilesDir(null), "windows-e2e-backup.zip")
                store.exportBackup(Uri.fromFile(exported))
                instrumentation.sendStatus(0, android.os.Bundle().apply {
                    putString("unishare_qa_item_id", item.id)
                    putString("unishare_qa_blob_sha256", store.listItems(query = "Android QA editado").single().assetSha256)
                })
            }
        }
    }
}
