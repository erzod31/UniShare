package org.unishare.app

import android.content.ContentValues
import android.net.Uri
import android.os.Build
import android.provider.MediaStore
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Assume.assumeTrue
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.security.MessageDigest
import java.util.UUID

@RunWith(AndroidJUnit4::class)
class AndroidPersistenceTest {
    private val base = InstrumentationRegistry.getInstrumentation().targetContext

    @Test
    fun offlineEditsOrganizationAndTrashSurviveDatabaseReopen() {
        IsolatedLibraryContext(base).use { context ->
            val id = LocalLibraryStore(context).use { store ->
                val item = store.saveLink("Original", "https://example.invalid/offline", "Lecturas")
                store.updateItem(item.id, "Título editado", "Origen", "Autora", "Nota propia", true,
                    "Pruebas, Trabajo", "Lecturas, Proyectos")
                store.setArchived(item.id, true)
                assertTrue(store.listItems().isEmpty())
                item.id
            }
            LocalLibraryStore(context).use { store ->
                val item = store.listItems(archivedOnly = true).single()
                assertEquals(id, item.id)
                assertEquals("Título editado", item.title)
                assertEquals("Nota propia", item.description)
                assertEquals(setOf("Pruebas", "Trabajo"), item.tags.toSet())
                assertEquals(setOf("Lecturas", "Proyectos"), item.collections.toSet())
                assertTrue(item.favorite)
                store.setArchived(id, false)
                assertEquals(id, store.listItems(query = "Trabajo", favoritesOnly = true,
                    collection = "proyectos").single().id)
                assertTrue(store.hasPendingDirectOperations())
            }
        }
    }

    @Test
    fun failedBatchRollsBackEveryItem() {
        IsolatedLibraryContext(base).use { context ->
            LocalLibraryStore(context).use { store ->
                val item = store.saveLink("Conservar", "https://example.invalid/rollback")
                assertTrue(runCatching {
                    store.setArchived(listOf(item.id, UUID.randomUUID().toString()), true)
                }.isFailure)
                assertEquals(item.id, store.listItems().single().id)
                assertTrue(store.listItems(archivedOnly = true).isEmpty())
            }
        }
    }

    @Test
    fun contentUriImportRemainsOfflineAfterTheOriginalIsDeleted() {
        assumeTrue(Build.VERSION.SDK_INT >= 29)
        val bytes = "Documento de prueba local\nSegunda línea.".toByteArray()
        val uri = createDocument(bytes, "text/plain", "unishare-${UUID.randomUUID()}.txt")
        try {
            IsolatedLibraryContext(base).use { context ->
                val id = LocalLibraryStore(context).use { store ->
                    val item = store.importFile(uri, "https://example.invalid/hybrid", "Archivos")
                    assertEquals(3, item.kind)
                    assertArrayEquals(bytes, store.localAsset(item.id)!!.file.readBytes())
                    item.id
                }
                base.contentResolver.delete(uri, null, null)
                LocalLibraryStore(context).use { store ->
                    val item = store.listItems(offlineOnly = true).single()
                    assertEquals(id, item.id)
                    assertEquals(listOf("Archivos"), item.collections)
                    assertArrayEquals(bytes, store.localAsset(id)!!.file.readBytes())
                }
            }
        } finally {
            base.contentResolver.delete(uri, null, null)
        }
    }

    @Test
    fun disguisedHtmlFromAContentProviderCannotCreateAnItemOrLeaveAPartialFile() {
        assumeTrue(Build.VERSION.SDK_INT >= 29)
        val uri = createDocument("<html><script>alert(1)</script></html>".toByteArray(),
            "application/pdf", "unishare-${UUID.randomUUID()}.pdf")
        try {
            IsolatedLibraryContext(base).use { context ->
                LocalLibraryStore(context).use { store ->
                    assertTrue(runCatching { store.importFile(uri) }.isFailure)
                    assertTrue(store.listItems().isEmpty())
                    assertFalse(store.hasPendingDirectOperations())
                    assertTrue(File(context.filesDir, "staging").listFiles().orEmpty().isEmpty())
                }
            }
        } finally {
            base.contentResolver.delete(uri, null, null)
        }
    }

    @Test
    fun portableBackupRestoresEveryFieldAndExactBlobIntoANewProfile() {
        IsolatedLibraryContext(base).use { source ->
            IsolatedLibraryContext(base).use { destination ->
                LocalLibraryStore(source).use { original ->
                    val item = original.saveLink("Híbrido", "https://example.invalid/backup", "Lecturas")
                    original.updateItem(item.id, item.title, "Fuente", "Autora", "Resumen\nhttps://example.invalid",
                        true, "uno, dos", "Lecturas, Archivo")
                    val bytes = ByteArray(131_072).also { java.util.Random(19).nextBytes(it) }
                    attach(original, source, item.id, bytes)
                    val backup = File(source.cacheDir, "roundtrip.zip")
                    val exported = original.exportBackup(Uri.fromFile(backup))
                    assertEquals(1, exported.blobCount)
                    LocalLibraryStore(destination).use { restored ->
                        val result = restored.restoreBackup(Uri.fromFile(backup))
                        assertEquals(1, result.importedItems)
                        val expected = original.listItems().single()
                        val actual = restored.listItems().single()
                        assertEquals(java.time.Instant.parse(expected.createdAt), java.time.Instant.parse(actual.createdAt))
                        assertEquals(expected.copy(createdAt = actual.createdAt), actual)
                        assertArrayEquals(bytes, restored.localAsset(item.id)!!.file.readBytes())
                        assertEquals(0, restored.restoreBackup(Uri.fromFile(backup)).conflicts)
                        assertEquals(1, restored.listItems().size)
                    }
                }
            }
        }
    }

    @Test
    fun requestedBlobIsExportedEvenAfterAllMetadataWasAcknowledged() {
        IsolatedLibraryContext(base).use { context ->
            LocalLibraryStore(context).use { store ->
                val item = store.saveLink("Reparación", "https://example.invalid/retransmit", "Conservar")
                attach(store, context, item.id, "Contenido recuperable".toByteArray())
                val hash = checkNotNull(store.listItems().single().assetSha256)
                val output = File(context.cacheDir, "delta.zip")

                val summary = PortableBackupExporter(context).export(store.readableDatabase, Uri.fromFile(output),
                    includedBlobHashes = setOf(hash), availableBlobHashes = setOf(hash), includedItemIds = emptySet())

                assertEquals(1, summary.itemCount)
                assertEquals(1, summary.blobCount)
                java.util.zip.ZipFile(output).use { archive ->
                    assertNotNull(archive.getEntry("blobs/${hash.take(2)}/${hash.substring(2, 4)}/$hash"))
                }
            }
        }
    }

    @Test
    fun concurrentEditsRemainExplicitUntilTheUserChoosesAVersion() {
        IsolatedLibraryContext(base).use { left ->
            IsolatedLibraryContext(base).use { right ->
                LocalLibraryStore(left).use { first ->
                    LocalLibraryStore(right).use { second ->
                        val item = first.saveLink("Original", "https://example.invalid/conflict")
                        val packageFile = File(left.cacheDir, "exchange.zip")
                        first.exportBackup(Uri.fromFile(packageFile))
                        second.restoreBackup(Uri.fromFile(packageFile))
                        first.updateItem(item.id, "Edición A", null, null, "Nota A", false, "", "")
                        second.updateItem(item.id, "Edición B", null, null, "Nota B", false, "", "")
                        second.exportBackup(Uri.fromFile(packageFile))
                        assertEquals(1, first.restoreBackup(Uri.fromFile(packageFile)).conflicts)
                        val conflict = first.listSyncConflicts().single()
                        assertEquals("Edición A", first.listItems().single().title)
                        assertEquals("Edición B", conflict.remoteTitle)
                        first.resolveSyncConflict(conflict.id, true)
                        assertTrue(first.listSyncConflicts().isEmpty())
                        assertEquals("Nota B", first.listItems().single().description)
                    }
                }
            }
        }
    }

    @Test
    fun versionOneMigratesThroughSQLiteOpenHelperWithoutLosingTheLink() {
        IsolatedLibraryContext(base).use { context ->
            val id = UUID.randomUUID().toString()
            context.openOrCreateDatabase("unishare.db", 0, null).use { database ->
                database.execSQL("""CREATE TABLE items(id TEXT PRIMARY KEY NOT NULL, kind INTEGER NOT NULL,
                    title TEXT NOT NULL, original_url TEXT, created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL, deleted_at TEXT)""")
                database.execSQL("INSERT INTO items VALUES(?,1,'Anterior','https://example.invalid/v1',?,?,NULL)",
                    arrayOf(id, "2026-01-01T00:00:00Z", "2026-01-01T00:00:00Z"))
                database.version = 1
            }
            LocalLibraryStore(context).use { migrated ->
                assertEquals(5, migrated.readableDatabase.version)
                assertEquals(id, migrated.listItems().single().id)
                assertEquals("Anterior", migrated.listItems().single().title)
                assertTrue(migrated.hasPendingDirectOperations())
            }
        }
    }

    @Test
    fun versionFourMigrationSeedsOperationsAndPreservesOrganization() {
        IsolatedLibraryContext(base).use { context ->
            val id = LocalLibraryStore(context).use { store ->
                val item = store.saveLink("Anterior", "https://example.invalid/v4", "Conservar")
                val database = store.writableDatabase
                val triggers = database.rawQuery("SELECT name FROM sqlite_master WHERE type='trigger'", null)
                    .use { cursor -> buildList { while (cursor.moveToNext()) add(cursor.getString(0)) } }
                triggers.forEach { database.execSQL("DROP TRIGGER $it") }
                database.execSQL("DROP TABLE sync_operations")
                database.delete("settings", "key='sync_importing'", null)
                database.version = 4
                item.id
            }
            LocalLibraryStore(context).use { migrated ->
                assertEquals(5, migrated.readableDatabase.version)
                assertEquals(id, migrated.listItems().single().id)
                assertEquals(listOf("Conservar"), migrated.listItems().single().collections)
                assertTrue(migrated.hasPendingDirectOperations())
            }
        }
    }

    private fun createDocument(bytes: ByteArray, mime: String, name: String): Uri {
        val uri = checkNotNull(base.contentResolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI,
            ContentValues().apply {
                put(MediaStore.MediaColumns.DISPLAY_NAME, name)
                put(MediaStore.MediaColumns.MIME_TYPE, mime)
                put(MediaStore.MediaColumns.IS_PENDING, 1)
            }))
        checkNotNull(base.contentResolver.openOutputStream(uri)).use { it.write(bytes) }
        base.contentResolver.update(uri, ContentValues().apply { put(MediaStore.MediaColumns.IS_PENDING, 0) }, null, null)
        return uri
    }

    companion object {
        internal fun attach(store: LocalLibraryStore, context: android.content.Context, id: String, bytes: ByteArray) {
            val file = File(context.cacheDir, "${UUID.randomUUID()}.bin").apply { writeBytes(bytes) }
            val hash = MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it) }
            store.attachDownloadedAsset(id, DownloadedContent(file, hash, bytes.size.toLong(),
                "application/octet-stream", "fixture.bin"))
        }
    }
}
