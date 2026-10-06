package org.unishare.app

import android.content.ContentValues
import android.content.Context
import android.database.Cursor
import android.database.sqlite.SQLiteDatabase
import android.net.Uri
import androidx.core.database.sqlite.transaction
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.security.MessageDigest
import java.time.Instant
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter
import java.util.UUID
import java.util.zip.ZipFile

data class AndroidSyncSummary(
    val sourceDeviceId: String,
    val importedItems: Int,
    val updatedItems: Int,
    val unchangedItems: Int,
    val conflicts: Int,
    val importedAssets: Int,
)

data class AndroidSyncConflict(
    val id: String,
    val localTitle: String,
    val remoteTitle: String,
    val createdAt: String,
)

class PortableBackupImporter(private val context: Context) {
    fun import(
        database: SQLiteDatabase,
        source: Uri,
        trackAsLocalChanges: Boolean = true,
    ): AndroidSyncSummary {
        val packageFile = copyPackage(source)
        try {
            ZipFile(packageFile).use { archive ->
                verifyArchive(archive)
                val manifest = readJson(archive, MANIFEST_PATH)
                require(manifest.getInt("format_version") == 1) { "Versión de paquete no compatible" }
                val sourceDeviceId = PortablePackageIdentity.sourceDeviceId(
                    manifest.optString("source_device_id").ifBlank { null },
                )
                val localDeviceId = readDeviceId(database)
                require(sourceDeviceId != localDeviceId) { "No se puede sincronizar un dispositivo consigo mismo" }
                val items = readNdjson(archive, ITEMS_PATH).map(::normalizeItem)
                val assets = readNdjson(archive, ASSETS_PATH).map(::normalizeAsset)
                val tags = readNdjson(archive, TAGS_PATH)
                val collections = readNdjson(archive, COLLECTIONS_PATH)
                val tagMemberships = readNdjson(archive, TAG_MEMBERSHIPS_PATH)
                val collectionMemberships = readNdjson(archive, COLLECTION_MEMBERSHIPS_PATH)
                require(items.size == manifest.getInt("item_count")) { "Recuento de elementos incorrecto" }
                require(assets.size == manifest.getInt("asset_count")) { "Recuento de archivos incorrecto" }
                require(tags.size == manifest.getInt("tag_count")) { "Recuento de etiquetas incorrecto" }
                require(collections.size == manifest.getInt("collection_count")) {
                    "Recuento de colecciones incorrecto"
                }
                validatePackageRelations(items, assets)
                val importedAssets = importBlobs(archive, assets)

                var imported = 0
                var updated = 0
                var unchanged = 0
                var conflicts = 0
                val now = Instant.now().toString()
                database.transaction {
                    setSyncImporting(database, importing = true)
                    items.forEach { remote ->
                        val itemId = remote.getString("id")
                        val remoteHash = hashJson(remote)
                        val local = readItem(database, itemId)
                        val localHash = local?.let(::hashJson)
                        val action = SyncMerge.decide(
                            localHash,
                            remoteHash,
                            readBaseline(database, sourceDeviceId, itemId),
                            hasConflict(database, sourceDeviceId, itemId),
                        )
                        when (action) {
                            SyncMergeAction.IMPORT -> {
                                insertItem(database, remote)
                                upsertBaseline(database, sourceDeviceId, itemId, remoteHash, remoteHash, now)
                                imported++
                            }
                            SyncMergeAction.UNCHANGED -> {
                                upsertBaseline(database, sourceDeviceId, itemId, remoteHash, remoteHash, now)
                                unchanged++
                            }
                            SyncMergeAction.APPLY_REMOTE -> {
                                updateItem(database, remote)
                                upsertBaseline(database, sourceDeviceId, itemId, remoteHash, remoteHash, now)
                                updated++
                            }
                            SyncMergeAction.KEEP_LOCAL -> {
                                upsertBaseline(database, sourceDeviceId, itemId, localHash!!, remoteHash, now)
                                unchanged++
                            }
                            SyncMergeAction.CONFLICT -> {
                                upsertConflict(
                                    database, sourceDeviceId, local!!, localHash!!, remote, remoteHash, now,
                                )
                                conflicts++
                            }
                        }
                    }
                    assets.forEach { insertAssetIfMissing(database, it) }
                    val tagIds = ensureLabels(database, "tags", tags)
                    val collectionIds = ensureLabels(database, "collections", collections)
                    tagMemberships.forEach {
                        insertMembershipIfMissing(database, "item_tags", "tag_id", it, tagIds)
                    }
                    collectionMemberships.forEach {
                        insertMembershipIfMissing(
                            database, "item_collections", "collection_id", it, collectionIds,
                        )
                    }
                    if (trackAsLocalChanges) {
                        items.forEach { remote -> recordSyncOperation(database, remote.getString("id"), now) }
                    }
                    setSyncImporting(database, importing = false)
                }
                return AndroidSyncSummary(
                    sourceDeviceId, imported, updated, unchanged, conflicts, importedAssets,
                )
            }
        } finally {
            packageFile.delete()
        }
    }

    fun listConflicts(database: SQLiteDatabase): List<AndroidSyncConflict> {
        val result = mutableListOf<AndroidSyncConflict>()
        database.rawQuery(
            """
            SELECT id, local_json, remote_json, created_at FROM sync_conflicts
            WHERE resolved_at IS NULL ORDER BY created_at DESC
            """.trimIndent(), null,
        ).use { cursor ->
            while (cursor.moveToNext()) {
                result += AndroidSyncConflict(
                    cursor.getString(0),
                    JSONObject(cursor.getString(1)).getString("title"),
                    JSONObject(cursor.getString(2)).getString("title"),
                    cursor.getString(3),
                )
            }
        }
        return result
    }

    fun resolveConflict(database: SQLiteDatabase, conflictId: String, useRemote: Boolean) {
        database.transaction {
            database.rawQuery(
                """
                SELECT item_id, peer_device_id, local_json, remote_json, remote_hash
                FROM sync_conflicts WHERE id=? AND resolved_at IS NULL
                """.trimIndent(), arrayOf(conflictId),
            ).use { cursor ->
                require(cursor.moveToFirst()) { "El conflicto no existe o ya está resuelto" }
                val itemId = cursor.getString(0)
                val peerId = cursor.getString(1)
                val local = JSONObject(cursor.getString(2))
                val remote = JSONObject(cursor.getString(3))
                val remoteHash = cursor.getString(4)
                val chosen = if (useRemote) remote else local
                if (useRemote) updateItem(database, chosen)
                val now = Instant.now().toString()
                upsertBaseline(database, peerId, itemId, hashJson(chosen), remoteHash, now)
                val values = ContentValues().apply {
                    put("resolved_at", now)
                    put("resolution", if (useRemote) "remote" else "local")
                }
                database.update("sync_conflicts", values, "id=?", arrayOf(conflictId))
            }
        }
    }

    private fun setSyncImporting(database: SQLiteDatabase, importing: Boolean) {
        val values = ContentValues().apply { put("value", if (importing) "1" else "0") }
        check(database.update("settings", values, "key='sync_importing'", null) == 1) {
            "Falta el control de importación de sincronización"
        }
    }

    private fun recordSyncOperation(database: SQLiteDatabase, itemId: String, createdAt: String) {
        database.insertOrThrow("sync_operations", null, ContentValues().apply {
            put("item_id", itemId)
            put("created_at", createdAt)
        })
    }

    private fun copyPackage(source: Uri): File {
        val destination = File(context.cacheDir, "sync-${UUID.randomUUID()}.zip")
        var total = 0L
        context.contentResolver.openInputStream(source)?.use { input ->
            FileOutputStream(destination).use { output ->
                val buffer = ByteArray(BUFFER_SIZE)
                while (true) {
                    val count = input.read(buffer)
                    if (count < 0) break
                    total += count
                    require(total <= MAX_PACKAGE_BYTES) { "El paquete supera el límite admitido" }
                    output.write(buffer, 0, count)
                }
                output.fd.sync()
            }
        } ?: throw IllegalArgumentException("No se pudo abrir el paquete")
        return destination
    }

    private fun verifyArchive(archive: ZipFile) {
        val entries = archive.entries().toList()
        require(entries.size <= MAX_ENTRIES) { "El paquete contiene demasiadas entradas" }
        val paths = entries.map { it.name }
        require(paths.distinct().size == paths.size) { "El paquete contiene rutas duplicadas" }
        paths.forEach { path ->
            require(
                path.isNotBlank() && !path.startsWith('/') && !path.contains('\\') &&
                    !path.contains(':') && path.split('/').none { it == ".." || it.isBlank() },
            ) { "Ruta no segura en el paquete" }
        }
        REQUIRED_PATHS.forEach { require(paths.contains(it)) { "Falta $it" } }
        val checksums = archive.getInputStream(archive.getEntry(CHECKSUM_PATH)).bufferedReader().useLines { lines ->
            lines.filter { it.isNotBlank() }.associate { line ->
                require(line.length > 66 && line.substring(64, 66) == "  ") { "Índice de integridad inválido" }
                line.substring(66) to line.substring(0, 64)
            }
        }
        require(checksums.keys == paths.filter { it != CHECKSUM_PATH }.toSet()) {
            "El índice de integridad no cubre exactamente el paquete"
        }
        checksums.forEach { (path, expected) ->
            require(HASH_PATTERN.matches(expected)) { "SHA-256 inválido en el índice" }
            val actual = digestEntry(archive, path)
            require(MessageDigest.isEqual(actual.toByteArray(), expected.toByteArray())) {
                "La entrada $path está dañada"
            }
        }
    }

    private fun digestEntry(archive: ZipFile, path: String): String {
        val digest = MessageDigest.getInstance("SHA-256")
        var total = 0L
        archive.getInputStream(archive.getEntry(path)).use { input ->
            val buffer = ByteArray(BUFFER_SIZE)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                total += count
                require(total <= MAX_ENTRY_BYTES) { "Una entrada del paquete es demasiado grande" }
                digest.update(buffer, 0, count)
            }
        }
        return digest.digest().toHex()
    }

    private fun readJson(archive: ZipFile, path: String): JSONObject =
        JSONObject(archive.getInputStream(archive.getEntry(path)).bufferedReader().readText())

    private fun readNdjson(archive: ZipFile, path: String): List<JSONObject> =
        archive.getInputStream(archive.getEntry(path)).bufferedReader().useLines { lines ->
            lines.filter { it.isNotBlank() }.map(::JSONObject).toList()
        }

    private fun importBlobs(archive: ZipFile, assets: List<JSONObject>): Int {
        var imported = 0
        assets.distinctBy { it.getString("sha256") }.forEach { asset ->
            val hash = asset.getString("sha256")
            val relativePath = blobPath(hash)
            val destination = File(context.filesDir, relativePath)
            if (destination.exists()) {
                require(hashFile(destination) == hash) { "Existe un blob local dañado: $hash" }
                return@forEach
            }
            destination.parentFile?.mkdirs()
            val partial = File(destination.parentFile, "$hash.partial-${UUID.randomUUID()}")
            try {
                archive.getInputStream(archive.getEntry(relativePath)).use { input ->
                    FileOutputStream(partial).use { output ->
                        input.copyTo(output, BUFFER_SIZE)
                        output.fd.sync()
                    }
                }
                require(partial.length() == asset.getLong("byte_length") && hashFile(partial) == hash) {
                    "El blob $hash no coincide con sus metadatos"
                }
                require(partial.renameTo(destination)) { "No se pudo conservar el blob $hash" }
                imported++
            } finally {
                partial.delete()
            }
        }
        return imported
    }

    private fun normalizeItem(raw: JSONObject): JSONObject {
        val id = UUID.fromString(raw.getString("id")).toString()
        val kind = raw.getInt("kind")
        require(kind in 1..3) { "Tipo de elemento remoto inválido" }
        val title = raw.getString("title").trim()
        require(title.isNotEmpty() && title.length <= 500) { "Título remoto inválido" }
        val originalUrl = raw.optionalString("original_url")?.trim()?.ifEmpty { null }
        require(if (kind == 2) originalUrl == null else originalUrl?.let(SharePayload::isWebUrl) == true) {
            "URL remota inválida"
        }
        return JSONObject()
            .put("id", id).put("kind", kind).put("title", title)
            .putNullable("original_url", originalUrl)
            .putNullable(
                "canonical_url",
                raw.optionalString("canonical_url")?.trim()?.ifEmpty { null }
                    ?: originalUrl?.let(SharePayload::canonicalize),
            )
            .putNullable("source", raw.optionalString("source")?.trim()?.ifEmpty { null })
            .putNullable("author", raw.optionalString("author")?.trim()?.ifEmpty { null })
            .putNullable("description", raw.optionalString("description")?.trim()?.ifEmpty { null })
            .put("created_at", portableTimestamp(raw.getString("created_at")))
            .put("updated_at", portableTimestamp(raw.getString("updated_at")))
            .putNullable("deleted_at", raw.optionalString("deleted_at")?.let(::portableTimestamp))
            .put("favorite", raw.optBoolean("favorite", false))
    }

    private fun normalizeAsset(raw: JSONObject): JSONObject {
        val id = UUID.fromString(raw.getString("id")).toString()
        val itemId = UUID.fromString(raw.getString("item_id")).toString()
        val hash = raw.getString("sha256")
        require(HASH_PATTERN.matches(hash) && raw.getString("relative_path") == blobPath(hash)) {
            "Manifest de archivo inválido"
        }
        require(raw.getLong("byte_length") >= 0) { "Tamaño de archivo inválido" }
        return JSONObject(raw.toString()).put("id", id).put("item_id", itemId)
            .put("created_at", portableTimestamp(raw.getString("created_at")))
    }

    private fun validatePackageRelations(items: List<JSONObject>, assets: List<JSONObject>) {
        val itemIds = items.map { it.getString("id") }.toSet()
        val itemIdsWithAssets = mutableSetOf<String>()
        assets.forEach { asset ->
            val itemId = asset.getString("item_id")
            require(itemId in itemIds) { "Un archivo remoto referencia un elemento inexistente" }
            itemIdsWithAssets += itemId
        }
        items.filter { it.getInt("kind") in 2..3 }.forEach { item ->
            require(item.getString("id") in itemIdsWithAssets) {
                "Un elemento de archivo o híbrido no contiene ningún archivo"
            }
        }
    }

    private fun readItem(database: SQLiteDatabase, id: String): JSONObject? =
        database.rawQuery(
            """
            SELECT id, kind, title, original_url, canonical_url, source, author, description,
                   created_at, updated_at, deleted_at, favorite FROM items WHERE id=?
            """.trimIndent(), arrayOf(id),
        ).use { cursor -> if (cursor.moveToFirst()) cursor.toItemJson() else null }

    private fun Cursor.toItemJson(): JSONObject = JSONObject()
        .put("id", getString(0)).put("kind", getInt(1)).put("title", getString(2))
        .putNullable("original_url", stringOrNull(3)).putNullable(
            "canonical_url", stringOrNull(4) ?: stringOrNull(3)?.let(SharePayload::canonicalize),
        )
        .putNullable("source", stringOrNull(5)).putNullable("author", stringOrNull(6))
        .putNullable("description", stringOrNull(7)).put("created_at", portableTimestamp(getString(8)))
        .put("updated_at", portableTimestamp(getString(9)))
        .putNullable("deleted_at", stringOrNull(10)?.let(::portableTimestamp))
        .put("favorite", getInt(11) == 1)

    private fun insertItem(database: SQLiteDatabase, item: JSONObject) {
        database.insertOrThrow("items", null, itemValues(item))
    }

    private fun updateItem(database: SQLiteDatabase, item: JSONObject) {
        require(database.update("items", itemValues(item), "id=?", arrayOf(item.getString("id"))) == 1) {
            "No se pudo actualizar el elemento sincronizado"
        }
    }

    private fun itemValues(item: JSONObject) = ContentValues().apply {
        put("id", item.getString("id")); put("kind", item.getInt("kind")); put("title", item.getString("title"))
        putNullable("original_url", item.optionalString("original_url"))
        putNullable("canonical_url", item.optionalString("canonical_url")); putNullable("source", item.optionalString("source"))
        putNullable("author", item.optionalString("author")); putNullable("description", item.optionalString("description"))
        put("created_at", item.getString("created_at")); put("updated_at", item.getString("updated_at"))
        putNullable("deleted_at", item.optionalString("deleted_at")); put("favorite", if (item.getBoolean("favorite")) 1 else 0)
    }

    private fun insertAssetIfMissing(database: SQLiteDatabase, asset: JSONObject) {
        val id = asset.getString("id")
        database.rawQuery("SELECT sha256, item_id FROM assets WHERE id=?", arrayOf(id)).use { cursor ->
            if (cursor.moveToFirst()) {
                require(cursor.getString(0) == asset.getString("sha256") && cursor.getString(1) == asset.getString("item_id")) {
                    "Colisión de identidad de archivo"
                }
                return
            }
        }
        val values = ContentValues().apply {
            put("id", id); put("item_id", asset.getString("item_id")); put("sha256", asset.getString("sha256"))
            put("byte_length", asset.getLong("byte_length")); put("mime_type", asset.getString("mime_type"))
            put("original_name", asset.getString("original_name")); put("relative_path", asset.getString("relative_path"))
            put("created_at", asset.getString("created_at"))
        }
        database.insertOrThrow("assets", null, values)
    }

    private fun ensureLabels(
        database: SQLiteDatabase,
        table: String,
        labels: List<JSONObject>,
    ): Map<String, String> {
        val result = mutableMapOf<String, String>()
        labels.forEach { label ->
            val remoteId = UUID.fromString(label.getString("id")).toString()
            val name = OrganizationNames.normalize(label.getString("name"))
            require(name.normalized == label.getString("normalized_name")) {
                "Etiqueta o colección sin normalización canónica"
            }
            var localId: String? = null
            database.rawQuery(
                "SELECT id FROM $table WHERE normalized_name=?", arrayOf(name.normalized),
            ).use { cursor -> if (cursor.moveToFirst()) localId = cursor.getString(0) }
            if (localId == null) {
                val values = ContentValues().apply {
                    put("id", remoteId); put("name", name.display); put("normalized_name", name.normalized)
                }
                database.insertOrThrow(table, null, values)
                localId = remoteId
            }
            result[remoteId] = requireNotNull(localId)
        }
        return result
    }

    private fun insertMembershipIfMissing(
        database: SQLiteDatabase,
        table: String,
        labelColumn: String,
        membership: JSONObject,
        labelIds: Map<String, String>,
    ) {
        val membershipId = UUID.fromString(membership.getString("membership_id")).toString()
        val itemId = UUID.fromString(membership.getString("item_id")).toString()
        val remoteLabelId = UUID.fromString(membership.getString("label_id")).toString()
        val localLabelId = requireNotNull(labelIds[remoteLabelId]) {
            "Una membresía referencia una etiqueta inexistente"
        }
        val remoteRemovedAt = membership.optionalString("removed_at")?.let(::portableTimestamp)
        var applyRemoteRemoval = false
        database.rawQuery(
            "SELECT item_id,$labelColumn,removed_at FROM $table WHERE membership_id=?", arrayOf(membershipId),
        ).use { cursor ->
            if (cursor.moveToFirst()) {
                require(cursor.getString(0) == itemId && cursor.getString(1) == localLabelId) {
                    "Colisión de identidad de membresía"
                }
                val localRemovedAt = cursor.stringOrNull(2)
                applyRemoteRemoval = localRemovedAt == null &&
                    MembershipRemoval.merge(localRemovedAt, remoteRemovedAt) != null
                if (!applyRemoteRemoval) return
            }
        }
        if (applyRemoteRemoval) {
            val removed = ContentValues().apply { put("removed_at", remoteRemovedAt) }
            database.update(
                table, removed, "membership_id=? AND removed_at IS NULL", arrayOf(membershipId),
            )
            return
        }
        val values = ContentValues().apply {
            put("membership_id", membershipId); put("item_id", itemId); put(labelColumn, localLabelId)
            putNullable("removed_at", remoteRemovedAt)
        }
        database.insertOrThrow(table, null, values)
    }

    private fun readBaseline(database: SQLiteDatabase, peer: String, item: String): SyncBaseline? =
        database.rawQuery(
            "SELECT local_hash, remote_hash FROM sync_item_state WHERE peer_device_id=? AND item_id=?",
            arrayOf(peer, item),
        ).use { cursor -> if (cursor.moveToFirst()) SyncBaseline(cursor.getString(0), cursor.getString(1)) else null }

    private fun upsertBaseline(
        database: SQLiteDatabase, peer: String, item: String, local: String, remote: String, at: String,
    ) {
        database.execSQL(
            """
            INSERT INTO sync_item_state(peer_device_id,item_id,local_hash,remote_hash,last_synced_at)
            VALUES(?,?,?,?,?) ON CONFLICT(peer_device_id,item_id) DO UPDATE SET
            local_hash=excluded.local_hash,remote_hash=excluded.remote_hash,last_synced_at=excluded.last_synced_at
            """.trimIndent(), arrayOf(peer, item, local, remote, at),
        )
    }

    private fun hasConflict(database: SQLiteDatabase, peer: String, item: String): Boolean =
        database.rawQuery(
            "SELECT 1 FROM sync_conflicts WHERE peer_device_id=? AND item_id=? AND resolved_at IS NULL LIMIT 1",
            arrayOf(peer, item),
        ).use { it.moveToFirst() }

    private fun upsertConflict(
        database: SQLiteDatabase,
        peer: String,
        local: JSONObject,
        localHash: String,
        remote: JSONObject,
        remoteHash: String,
        at: String,
    ) {
        val values = ContentValues().apply {
            put("local_json", local.toString()); put("remote_json", remote.toString())
            put("local_hash", localHash); put("remote_hash", remoteHash); put("created_at", at)
        }
        if (database.update(
                "sync_conflicts", values,
                "peer_device_id=? AND item_id=? AND resolved_at IS NULL",
                arrayOf(peer, remote.getString("id")),
            ) == 0
        ) {
            values.put("id", UUID.randomUUID().toString()); values.put("item_id", remote.getString("id"))
            values.put("peer_device_id", peer)
            database.insertOrThrow("sync_conflicts", null, values)
        }
    }

    private fun readDeviceId(database: SQLiteDatabase): String =
        database.rawQuery("SELECT value FROM settings WHERE key='device_id'", null).use { cursor ->
            check(cursor.moveToFirst()) { "Falta la identidad local" }
            UUID.fromString(cursor.getString(0)).toString()
        }

    private fun hashJson(value: JSONObject): String =
        MessageDigest.getInstance("SHA-256").digest(value.toString().toByteArray()).toHex()

    private fun hashFile(file: File): String = file.inputStream().buffered().use { input ->
        val digest = MessageDigest.getInstance("SHA-256")
        val buffer = ByteArray(BUFFER_SIZE)
        while (true) {
            val count = input.read(buffer)
            if (count < 0) break
            digest.update(buffer, 0, count)
        }
        digest.digest().toHex()
    }

    private fun blobPath(hash: String) = "blobs/${hash.substring(0, 2)}/${hash.substring(2, 4)}/$hash"
    private fun portableTimestamp(raw: String): String = PORTABLE_TIMESTAMP.format(Instant.parse(raw))
    private fun ByteArray.toHex() = joinToString("") { "%02x".format(it.toInt() and 0xff) }
    private fun Cursor.stringOrNull(index: Int): String? = if (isNull(index)) null else getString(index)
    private fun JSONObject.optionalString(name: String): String? = if (isNull(name) || !has(name)) null else getString(name)
    private fun JSONObject.putNullable(name: String, value: String?): JSONObject = put(name, value ?: JSONObject.NULL)
    private fun ContentValues.putNullable(name: String, value: String?) = if (value == null) putNull(name) else put(name, value)

    companion object {
        private const val BUFFER_SIZE = 128 * 1024
        private const val MAX_PACKAGE_BYTES = 10L * 1024 * 1024 * 1024
        private const val MAX_ENTRY_BYTES = 10L * 1024 * 1024 * 1024
        private const val MAX_ENTRIES = 100_000
        private const val MANIFEST_PATH = "manifest.json"
        private const val CHECKSUM_PATH = "checksums.sha256"
        private const val ITEMS_PATH = "metadata/items.ndjson"
        private const val ASSETS_PATH = "metadata/assets.ndjson"
        private const val TAGS_PATH = "metadata/tags.ndjson"
        private const val COLLECTIONS_PATH = "metadata/collections.ndjson"
        private const val TAG_MEMBERSHIPS_PATH = "metadata/tag_memberships.ndjson"
        private const val COLLECTION_MEMBERSHIPS_PATH = "metadata/collection_memberships.ndjson"
        private val HASH_PATTERN = Regex("^[0-9a-f]{64}$")
        private val PORTABLE_TIMESTAMP =
            DateTimeFormatter.ofPattern("uuuu-MM-dd'T'HH:mm:ss.SSSSSSSxxx").withZone(ZoneOffset.UTC)
        private val REQUIRED_PATHS = setOf(
            MANIFEST_PATH, CHECKSUM_PATH, ITEMS_PATH, ASSETS_PATH,
            "metadata/tags.ndjson", "metadata/collections.ndjson",
            "metadata/tag_memberships.ndjson", "metadata/collection_memberships.ndjson",
        )
    }
}
