package org.unishare.app

import android.content.Context
import android.database.Cursor
import android.database.sqlite.SQLiteDatabase
import android.net.Uri
import androidx.core.database.sqlite.transaction
import org.json.JSONObject
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.io.OutputStream
import java.security.MessageDigest
import java.time.Instant
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.util.TreeMap
import java.util.UUID
import java.util.zip.ZipEntry
import java.util.zip.ZipOutputStream

data class AndroidBackupSummary(
    val itemCount: Int,
    val assetCount: Int,
    val blobCount: Int,
    val totalBlobBytes: Long,
)

class PortableBackupExporter(private val context: Context) {
    fun export(
        database: SQLiteDatabase,
        destination: Uri,
        includedBlobHashes: Set<String>? = null,
        availableBlobHashes: Set<String>? = null,
        includedItemIds: Set<String>? = null,
    ): AndroidBackupSummary {
        val partial = File(context.cacheDir, "backup-${UUID.randomUUID()}.partial")
        try {
            val summary = database.transaction {
                FileOutputStream(partial).use { fileOutput ->
                    val result = writeArchive(
                        database, fileOutput, includedBlobHashes, availableBlobHashes, includedItemIds,
                    )
                    fileOutput.fd.sync()
                    result
                }
            }
            context.contentResolver.openOutputStream(destination, "w")?.use { output ->
                FileInputStream(partial).use { input -> input.copyTo(output, BUFFER_SIZE) }
                output.flush()
            } ?: throw IllegalArgumentException("No se pudo crear el respaldo seleccionado")
            return summary
        } finally {
            partial.delete()
        }
    }

    private fun writeArchive(
        database: SQLiteDatabase,
        output: OutputStream,
        includedBlobHashes: Set<String>?,
        availableBlobHashes: Set<String>?,
        includedItemIds: Set<String>?,
    ): AndroidBackupSummary {
        val checksums = TreeMap<String, String>()
        var itemCount = 0
        var assetCount = 0
        var blobCount = 0
        var totalBlobBytes = 0L
        var tagCount = 0
        var collectionCount = 0
        val includedTagIds = referencedLabelIds(database, "item_tags", "tag_id", includedItemIds)
        val includedCollectionIds = referencedLabelIds(
            database, "item_collections", "collection_id", includedItemIds,
        )
        ZipOutputStream(NonClosingOutputStream(output).buffered(BUFFER_SIZE)).use { archive ->
            val items = writeEntry(archive, "metadata/items.ndjson") { entry ->
                itemCount = writeItems(database, entry, includedItemIds)
            }
            checksums[items.path] = items.sha256

            val assets = writeEntry(archive, "metadata/assets.ndjson") { entry ->
                assetCount = writeAssets(database, entry, includedItemIds)
            }
            checksums[assets.path] = assets.sha256

            val tags = writeEntry(archive, "metadata/tags.ndjson") { entry ->
                tagCount = writeLabels(database, "tags", entry, includedTagIds)
            }
            checksums[tags.path] = tags.sha256
            val collections = writeEntry(archive, "metadata/collections.ndjson") { entry ->
                collectionCount = writeLabels(database, "collections", entry, includedCollectionIds)
            }
            checksums[collections.path] = collections.sha256
            val tagMemberships = writeEntry(archive, "metadata/tag_memberships.ndjson") { entry ->
                writeMemberships(database, "item_tags", "tag_id", entry, includedItemIds)
            }
            checksums[tagMemberships.path] = tagMemberships.sha256
            val collectionMemberships = writeEntry(archive, "metadata/collection_memberships.ndjson") { entry ->
                writeMemberships(database, "item_collections", "collection_id", entry, includedItemIds)
            }
            checksums[collectionMemberships.path] = collectionMemberships.sha256
            if (availableBlobHashes != null) {
                val inventory = writeEntry(archive, "sync/available_blobs.ndjson") { entry ->
                    availableBlobHashes.sorted().forEach { hash ->
                        validateHash(hash)
                        entry.writeUtf8(JSONObject().put("sha256", hash).toString() + "\n")
                    }
                }
                checksums[inventory.path] = inventory.sha256
            }

            val emittedBlobs = mutableSetOf<String>()
            database.rawQuery(
                "SELECT sha256, relative_path, item_id FROM assets ORDER BY sha256,item_id", null,
            ).use { cursor ->
                while (cursor.moveToNext()) {
                    val sha256 = cursor.getString(0)
                    validateHash(sha256)
                    if (includedItemIds != null && cursor.getString(2) !in includedItemIds) continue
                    if (includedBlobHashes != null && sha256 !in includedBlobHashes) continue
                    if (!emittedBlobs.add(sha256)) continue
                    val expectedPath = blobPath(sha256)
                    check(cursor.getString(1) == expectedPath) { "Ruta de asset no canónica" }
                    val source = File(context.filesDir, expectedPath)
                    check(source.isFile) { "Falta el blob $sha256" }
                    val blob = writeEntry(archive, expectedPath) { entry ->
                        FileInputStream(source).use { it.copyTo(entry, BUFFER_SIZE) }
                    }
                    check(blob.sha256 == sha256) { "El blob $sha256 está dañado" }
                    checksums[blob.path] = blob.sha256
                    blobCount++
                    totalBlobBytes += blob.bytes
                }
            }

            val manifestJson = JSONObject()
                .put("format_version", FORMAT_VERSION)
                .put("source_device_id", readDeviceId(database))
                .put("created_at_utc", portableTimestamp(Instant.now().toString()))
                .put("item_count", itemCount)
                .put("asset_count", assetCount)
                .put("tag_count", tagCount)
                .put("collection_count", collectionCount)
                .put("blob_count", blobCount)
                .put("total_blob_bytes", totalBlobBytes)
                .toString() + "\n"
            val manifest = writeEntry(archive, "manifest.json") { entry -> entry.writeUtf8(manifestJson) }
            checksums[manifest.path] = manifest.sha256

            writeEntry(archive, "checksums.sha256") { entry ->
                checksums.forEach { (path, hash) -> entry.writeUtf8("$hash  $path\n") }
            }
        }
        return AndroidBackupSummary(itemCount, assetCount, blobCount, totalBlobBytes)
    }

    private fun writeItems(
        database: SQLiteDatabase,
        output: OutputStream,
        includedItemIds: Set<String>?,
    ): Int {
        var count = 0
        database.rawQuery(
            """
            SELECT id, kind, title, original_url, canonical_url, source, author, description,
                   created_at, updated_at, deleted_at, favorite
            FROM items ORDER BY id
            """.trimIndent(), null,
        ).use { cursor ->
            while (cursor.moveToNext()) {
                if (includedItemIds != null && cursor.getString(0) !in includedItemIds) continue
                val record = JSONObject()
                    .put("id", cursor.getString(0))
                    .put("kind", cursor.getInt(1))
                    .put("title", cursor.getString(2))
                    .putNullable("original_url", cursor.stringOrNull(3))
                    .putNullable(
                        "canonical_url",
                        cursor.stringOrNull(4) ?: cursor.stringOrNull(3)?.let(SharePayload::canonicalize),
                    )
                    .putNullable("source", cursor.stringOrNull(5))
                    .putNullable("author", cursor.stringOrNull(6))
                    .putNullable("description", cursor.stringOrNull(7))
                    .put("created_at", portableTimestamp(cursor.getString(8)))
                    .put("updated_at", portableTimestamp(cursor.getString(9)))
                    .putNullable("deleted_at", cursor.stringOrNull(10)?.let(::portableTimestamp))
                    .put("favorite", cursor.getInt(11) == 1)
                output.writeUtf8(record.toString() + "\n")
                count++
            }
        }
        return count
    }

    private fun writeAssets(
        database: SQLiteDatabase,
        output: OutputStream,
        includedItemIds: Set<String>?,
    ): Int {
        var count = 0
        database.rawQuery(
            """
            SELECT a.id, a.item_id, a.sha256, a.byte_length, a.mime_type, a.original_name,
                   a.relative_path, COALESCE(a.created_at, i.created_at)
            FROM assets AS a JOIN items AS i ON i.id = a.item_id
            ORDER BY a.id
            """.trimIndent(), null,
        ).use { cursor ->
            while (cursor.moveToNext()) {
                if (includedItemIds != null && cursor.getString(1) !in includedItemIds) continue
                val sha256 = cursor.getString(2)
                validateHash(sha256)
                check(cursor.getString(6) == blobPath(sha256)) { "Ruta de asset no canónica" }
                val record = JSONObject()
                    .put("id", cursor.getString(0))
                    .put("item_id", cursor.getString(1))
                    .put("sha256", sha256)
                    .put("byte_length", cursor.getLong(3))
                    .put("mime_type", cursor.getString(4))
                    .put("original_name", cursor.getString(5))
                    .put("relative_path", cursor.getString(6))
                    .put("created_at", portableTimestamp(cursor.getString(7)))
                output.writeUtf8(record.toString() + "\n")
                count++
            }
        }
        return count
    }

    private fun writeLabels(
        database: SQLiteDatabase,
        table: String,
        output: OutputStream,
        includedLabelIds: Set<String>?,
    ): Int {
        var count = 0
        database.rawQuery("SELECT id,name,normalized_name FROM $table ORDER BY id", null).use { cursor ->
            while (cursor.moveToNext()) {
                if (includedLabelIds != null && cursor.getString(0) !in includedLabelIds) continue
                output.writeUtf8(
                    JSONObject().put("id", cursor.getString(0)).put("name", cursor.getString(1))
                        .put("normalized_name", cursor.getString(2)).toString() + "\n",
                )
                count++
            }
        }
        return count
    }

    private fun writeMemberships(
        database: SQLiteDatabase,
        table: String,
        labelColumn: String,
        output: OutputStream,
        includedItemIds: Set<String>?,
    ) {
        database.rawQuery(
            "SELECT membership_id,item_id,$labelColumn,removed_at FROM $table ORDER BY membership_id", null,
        ).use { cursor ->
            while (cursor.moveToNext()) {
                if (includedItemIds != null && cursor.getString(1) !in includedItemIds) continue
                output.writeUtf8(
                    JSONObject().put("membership_id", cursor.getString(0)).put("item_id", cursor.getString(1))
                        .put("label_id", cursor.getString(2)).putNullable("removed_at", cursor.stringOrNull(3))
                        .toString() + "\n",
                )
            }
        }
    }

    private fun referencedLabelIds(
        database: SQLiteDatabase,
        membershipTable: String,
        labelColumn: String,
        includedItemIds: Set<String>?,
    ): Set<String>? {
        if (includedItemIds == null) return null
        return buildSet {
            database.rawQuery(
                "SELECT item_id,$labelColumn FROM $membershipTable ORDER BY membership_id", null,
            ).use { cursor ->
                while (cursor.moveToNext()) {
                    if (cursor.getString(0) in includedItemIds) add(cursor.getString(1))
                }
            }
        }
    }

    private fun writeEntry(
        archive: ZipOutputStream,
        path: String,
        writer: (OutputStream) -> Unit,
    ): EntryResult {
        archive.putNextEntry(ZipEntry(path))
        val digest = MessageDigest.getInstance("SHA-256")
        var bytes = 0L
        val hashingOutput = object : OutputStream() {
            override fun write(value: Int) {
                archive.write(value)
                digest.update(value.toByte())
                bytes++
            }

            override fun write(buffer: ByteArray, offset: Int, length: Int) {
                archive.write(buffer, offset, length)
                digest.update(buffer, offset, length)
                bytes += length
            }
        }
        writer(hashingOutput)
        archive.closeEntry()
        return EntryResult(path, digest.digest().toHex(), bytes)
    }

    private fun portableTimestamp(raw: String): String =
        PORTABLE_TIMESTAMP.format(Instant.parse(raw))

    private fun readDeviceId(database: SQLiteDatabase): String =
        database.rawQuery("SELECT value FROM settings WHERE key='device_id'", null).use { cursor ->
            check(cursor.moveToFirst()) { "Falta la identidad del dispositivo" }
            UUID.fromString(cursor.getString(0)).toString()
        }

    private fun blobPath(hash: String): String =
        "blobs/${hash.substring(0, 2)}/${hash.substring(2, 4)}/$hash"

    private fun validateHash(hash: String) {
        require(HASH_PATTERN.matches(hash)) { "SHA-256 inválido" }
    }

    private fun JSONObject.putNullable(name: String, value: String?): JSONObject =
        put(name, value ?: JSONObject.NULL)

    private fun Cursor.stringOrNull(index: Int): String? = if (isNull(index)) null else getString(index)

    private fun OutputStream.writeUtf8(value: String) = write(value.toByteArray(Charsets.UTF_8))

    private fun ByteArray.toHex(): String = joinToString("") { "%02x".format(it.toInt() and 0xff) }

    private data class EntryResult(val path: String, val sha256: String, val bytes: Long)

    private class NonClosingOutputStream(private val delegate: OutputStream) : OutputStream() {
        override fun write(value: Int) = delegate.write(value)
        override fun write(buffer: ByteArray, offset: Int, length: Int) =
            delegate.write(buffer, offset, length)
        override fun flush() = delegate.flush()
        override fun close() = delegate.flush()
    }

    companion object {
        private const val FORMAT_VERSION = 1
        private const val BUFFER_SIZE = 128 * 1024
        private val HASH_PATTERN = Regex("^[0-9a-f]{64}$")
        private val PORTABLE_TIMESTAMP =
            DateTimeFormatter.ofPattern("uuuu-MM-dd'T'HH:mm:ss.SSSSSSSxxx").withZone(ZoneOffset.UTC)
    }
}
