package org.unishare.app

import android.content.ContentValues
import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper
import android.net.Uri
import android.provider.OpenableColumns
import androidx.core.database.sqlite.transaction
import java.io.File
import java.io.FileOutputStream
import java.security.MessageDigest
import java.time.Instant
import java.util.UUID

data class SavedItem(
    val id: String,
    val kind: Int,
    val title: String,
    val originalUrl: String?,
    val createdAt: String,
    val assetSha256: String?,
    val source: String? = null,
    val author: String? = null,
    val description: String? = null,
    val favorite: Boolean = false,
    val tags: List<String> = emptyList(),
    val collections: List<String> = emptyList(),
    val deletedAt: String? = null,
)

data class LocalAsset(val file: File, val mimeType: String)

class LocalLibraryStore(private val context: Context) : SQLiteOpenHelper(context, "unishare.db", null, 5) {
    private val directSyncClient = DirectSyncClient(context)
    override fun onConfigure(database: SQLiteDatabase) {
        database.setForeignKeyConstraintsEnabled(true)
    }

    override fun onCreate(database: SQLiteDatabase) {
        createItemsTable(database, "items")
        database.execSQL("CREATE INDEX items_created_at ON items(created_at DESC)")
        createAssetsTable(database)
        createSyncTables(database)
        createOrganizationTables(database)
        createOperationTracking(database, seedExistingItems = false)
    }

    override fun onUpgrade(database: SQLiteDatabase, oldVersion: Int, newVersion: Int) {
        var version = oldVersion
        if (version == 1) {
            migrateVersionOneToTwo(database)
            createSyncTables(database)
            version = 3
        }
        if (version == 2) {
            migrateVersionTwoToThree(database)
            version = 3
        }
        if (version == 3) {
            createOrganizationTables(database)
            version = 4
        }
        if (version == 4) {
            createOperationTracking(database, seedExistingItems = true)
            version = 5
        }
        check(version == newVersion) {
            "La base de datos requiere una migración explícita: $oldVersion → $newVersion"
        }
    }

    private fun migrateVersionOneToTwo(database: SQLiteDatabase) {
        createItemsTable(database, "items_v2")
        database.execSQL(
            """
            INSERT INTO items_v2(id, kind, title, original_url, created_at, updated_at, deleted_at)
            SELECT id, kind, title, original_url, created_at, updated_at, deleted_at FROM items
            """.trimIndent(),
        )
        database.execSQL("DROP TABLE items")
        database.execSQL("ALTER TABLE items_v2 RENAME TO items")
        database.execSQL("CREATE INDEX items_created_at ON items(created_at DESC)")
        createAssetsTable(database)
    }

    private fun migrateVersionTwoToThree(database: SQLiteDatabase) {
        database.execSQL("ALTER TABLE items ADD COLUMN canonical_url TEXT")
        database.execSQL("ALTER TABLE items ADD COLUMN source TEXT")
        database.execSQL("ALTER TABLE items ADD COLUMN author TEXT")
        database.execSQL("ALTER TABLE items ADD COLUMN description TEXT")
        database.execSQL("ALTER TABLE items ADD COLUMN favorite INTEGER NOT NULL DEFAULT 0")
        database.execSQL("ALTER TABLE assets ADD COLUMN created_at TEXT")
        database.execSQL(
            "UPDATE assets SET created_at=(SELECT created_at FROM items WHERE items.id=assets.item_id)",
        )
        createSyncTables(database)
    }

    fun saveLink(title: String, rawUrl: String, collections: String = ""): SavedItem {
        val url = rawUrl.trim()
        require(SharePayload.isWebUrl(url)) { "Usa un enlace http o https válido" }
        val normalizedTitle = normalizeTitle(title.ifBlank { SharePayload.titleFromUrl(url) })
        val now = Instant.now().toString()
        val item = SavedItem(UUID.randomUUID().toString(), 1, normalizedTitle, url, now, null)
        val collectionNames = OrganizationNames.parse(collections)
        val database = writableDatabase
        database.transaction {
            database.insertOrThrow("items", null, itemValues(item, now))
            replaceLabels(
                database, item.id, "collections", "item_collections", "collection_id", collectionNames, now,
            )
        }
        return item
    }

    fun importFile(uri: Uri, sourceUrl: String? = null, collections: String = ""): SavedItem {
        if (sourceUrl != null) require(SharePayload.isWebUrl(sourceUrl)) { "Enlace asociado inválido" }
        val collectionNames = OrganizationNames.parse(collections)
        val name = displayName(uri)
        val staging = File(context.filesDir, "staging").apply { mkdirs() }
        val partial = File(staging, "${UUID.randomUUID()}.partial")
        try {
            val digest = MessageDigest.getInstance("SHA-256")
            var length = 0L
            val header = ByteArray(FileContentPolicy.MAXIMUM_HEADER_BYTES)
            var headerLength = 0
            context.contentResolver.openInputStream(uri)?.use { input ->
                FileOutputStream(partial).use { fileOutput ->
                    val output = fileOutput.buffered()
                    val buffer = ByteArray(128 * 1024)
                    while (true) {
                        val count = input.read(buffer)
                        if (count < 0) break
                        length += count
                        require(length <= MAXIMUM_FILE_BYTES) { "El archivo supera el límite de 1 GiB" }
                        digest.update(buffer, 0, count)
                        if (headerLength < header.size) {
                            val headerCount = minOf(count, header.size - headerLength)
                            buffer.copyInto(header, headerLength, 0, headerCount)
                            headerLength += headerCount
                        }
                        output.write(buffer, 0, count)
                    }
                    output.flush()
                    fileOutput.fd.sync()
                }
            } ?: throw IllegalArgumentException("No se pudo abrir el archivo compartido")

            val inspectedContent = FileContentPolicy.inspect(
                header.copyOf(headerLength),
                context.contentResolver.getType(uri),
                name,
            )
            val sha256 = digest.digest().joinToString("") { "%02x".format(it.toInt() and 0xff) }
            val relativePath = "blobs/${sha256.substring(0, 2)}/${sha256.substring(2, 4)}/$sha256"
            val destination = File(context.filesDir, relativePath)
            destination.parentFile?.mkdirs()
            if (destination.exists()) {
                check(verifyHash(destination, sha256)) { "Existe un archivo local dañado con el mismo hash" }
            } else {
                check(partial.renameTo(destination)) { "No se pudo conservar el archivo local" }
            }

            val now = Instant.now().toString()
            val item = SavedItem(UUID.randomUUID().toString(), if (sourceUrl == null) 2 else 3,
                normalizeTitle(name), sourceUrl, now, sha256)
            val database = writableDatabase
            database.transaction {
                database.insertOrThrow("items", null, itemValues(item, now))
                val asset = ContentValues().apply {
                    put("id", UUID.randomUUID().toString())
                    put("item_id", item.id)
                    put("sha256", sha256)
                    put("byte_length", length)
                    put("mime_type", inspectedContent.mimeType)
                    put("original_name", name)
                    put("relative_path", relativePath)
                    put("created_at", now)
                }
                database.insertOrThrow("assets", null, asset)
                replaceLabels(
                    database, item.id, "collections", "item_collections", "collection_id", collectionNames, now,
                )
            }
            return item
        } finally {
            partial.delete()
        }
    }

    fun listItems(
        query: String = "",
        favoritesOnly: Boolean = false,
        archivedOnly: Boolean = false,
        collection: String? = null,
        offlineOnly: Boolean = false,
        limit: Int = 201,
    ): List<SavedItem> {
        require(limit > 0) { "El límite de elementos no es válido" }
        val pattern = "%${query.trim().replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_")}%"
        val clauses = mutableListOf<String>()
        val arguments = mutableListOf<String>()
        clauses += if (archivedOnly) "i.deleted_at IS NOT NULL" else "i.deleted_at IS NULL"
        if (favoritesOnly) clauses += "i.favorite = 1"
        if (offlineOnly) clauses += "EXISTS (SELECT 1 FROM assets a WHERE a.item_id=i.id)"
        if (!collection.isNullOrBlank()) {
            clauses += """
                EXISTS (SELECT 1 FROM item_collections ic JOIN collections c ON c.id=ic.collection_id
                        WHERE ic.item_id=i.id AND ic.removed_at IS NULL AND c.normalized_name=?)
            """.trimIndent()
            arguments += OrganizationNames.normalize(collection).normalized
        }
        if (query.isNotBlank()) {
            clauses += """
                (i.title LIKE ? ESCAPE '\' OR i.original_url LIKE ? ESCAPE '\'
                 OR i.source LIKE ? ESCAPE '\' OR i.author LIKE ? ESCAPE '\'
                 OR i.description LIKE ? ESCAPE '\'
                 OR EXISTS (SELECT 1 FROM item_tags it JOIN tags t ON t.id=it.tag_id
                            WHERE it.item_id=i.id AND it.removed_at IS NULL AND t.name LIKE ? ESCAPE '\')
                 OR EXISTS (SELECT 1 FROM item_collections ic JOIN collections c ON c.id=ic.collection_id
                            WHERE ic.item_id=i.id AND ic.removed_at IS NULL AND c.name LIKE ? ESCAPE '\'))
            """.trimIndent()
            repeat(7) { arguments += pattern }
        }
        val result = mutableListOf<SavedItem>()
        readableDatabase.rawQuery(
            """
            SELECT i.id, i.kind, i.title, i.original_url, i.created_at,
                   (SELECT a.sha256 FROM assets a WHERE a.item_id=i.id ORDER BY a.id LIMIT 1),
                   i.source, i.author, i.description, i.favorite, i.deleted_at
            FROM items AS i
            ${if (clauses.isEmpty()) "" else "WHERE ${clauses.joinToString(" AND ")}"}
            ORDER BY i.created_at DESC
            LIMIT ?
            """.trimIndent(),
            (arguments + limit.toString()).toTypedArray(),
        ).use { cursor ->
            while (cursor.moveToNext()) {
                val itemId = cursor.getString(0)
                result += SavedItem(
                    itemId, cursor.getInt(1), cursor.getString(2), cursor.getString(3),
                    cursor.getString(4), cursor.getString(5), cursor.getString(6), cursor.getString(7),
                    cursor.getString(8), cursor.getInt(9) == 1,
                    deletedAt = cursor.getString(10),
                )
            }
        }
        val tags = labelsForItems(result.map { it.id }, "tags", "item_tags", "tag_id")
        val collections = labelsForItems(
            result.map { it.id }, "collections", "item_collections", "collection_id",
        )
        return result.map { item ->
            item.copy(tags = tags[item.id].orEmpty(), collections = collections[item.id].orEmpty())
        }
    }

    fun listItemsForMetadataRefresh(): List<SavedItem> {
        val result = mutableListOf<SavedItem>()
        readableDatabase.rawQuery(
            """
            SELECT i.id, i.kind, i.title, i.original_url, i.created_at,
                   (SELECT a.sha256 FROM assets a WHERE a.item_id=i.id ORDER BY a.id LIMIT 1),
                   i.source, i.author, i.description, i.favorite, i.deleted_at
            FROM items AS i
            WHERE i.original_url IS NOT NULL AND i.deleted_at IS NULL
            ORDER BY i.created_at DESC
            """.trimIndent(),
            null,
        ).use { cursor ->
            while (cursor.moveToNext()) {
                result += SavedItem(
                    cursor.getString(0), cursor.getInt(1), cursor.getString(2), cursor.getString(3),
                    cursor.getString(4), cursor.getString(5), cursor.getString(6), cursor.getString(7),
                    cursor.getString(8), cursor.getInt(9) == 1,
                    deletedAt = cursor.getString(10),
                )
            }
        }
        return result
    }

    fun listCollections(): List<Pair<String, Int>> {
        val result = mutableListOf<Pair<String, Int>>()
        readableDatabase.rawQuery(
            """
            SELECT c.name,COUNT(DISTINCT ic.item_id)
            FROM collections c
            JOIN item_collections ic ON ic.collection_id=c.id AND ic.removed_at IS NULL
            JOIN items i ON i.id=ic.item_id AND i.deleted_at IS NULL
            GROUP BY c.id,c.name,c.normalized_name
            HAVING COUNT(DISTINCT ic.item_id)>0
            ORDER BY c.normalized_name
            """.trimIndent(),
            null,
        ).use { cursor ->
            while (cursor.moveToNext()) result += cursor.getString(0) to cursor.getInt(1)
        }
        return result
    }

    fun updateItem(
        itemId: String,
        title: String,
        source: String?,
        author: String?,
        description: String?,
        favorite: Boolean,
        tags: String,
        collections: String,
    ) {
        val tagNames = OrganizationNames.parse(tags)
        val collectionNames = OrganizationNames.parse(collections)
        val now = Instant.now().toString()
        val database = writableDatabase
        database.transaction {
            val values = ContentValues().apply {
                put("title", normalizeTitle(title))
                putNullable("source", source)
                putNullable("author", author)
                putNullable("description", description)
                put("favorite", if (favorite) 1 else 0)
                put("updated_at", now)
            }
            require(database.update("items", values, "id=?", arrayOf(itemId)) == 1) {
                "El elemento ya no existe"
            }
            replaceLabels(database, itemId, "tags", "item_tags", "tag_id", tagNames, now)
            replaceLabels(
                database, itemId, "collections", "item_collections", "collection_id", collectionNames, now,
            )
        }
    }

    fun setArchived(itemId: String, archived: Boolean) = setArchived(listOf(itemId), archived)

    fun setArchived(itemIds: Collection<String>, archived: Boolean) {
        val uniqueIds = itemIds.distinct()
        require(uniqueIds.isNotEmpty()) { "Selecciona al menos un elemento" }
        val now = Instant.now().toString()
        val database = writableDatabase
        database.transaction {
            uniqueIds.forEach { itemId ->
                val values = ContentValues().apply {
                    if (archived) put("deleted_at", now) else putNull("deleted_at")
                    put("updated_at", now)
                }
                require(database.update("items", values, "id=?", arrayOf(itemId)) == 1) {
                    "El elemento ya no existe"
                }
            }
        }
    }

    fun localAsset(itemId: String): LocalAsset? = readableDatabase.rawQuery(
        "SELECT relative_path,mime_type FROM assets WHERE item_id=? ORDER BY id LIMIT 1",
        arrayOf(itemId),
    ).use { cursor ->
        if (!cursor.moveToFirst()) return@use null
        val file = File(context.filesDir, cursor.getString(0))
        if (file.isFile) LocalAsset(file, cursor.getString(1)) else null
    }

    fun attachDownloadedAsset(itemId: String, content: DownloadedContent) {
        require(content.file.isFile && verifyHash(content.file, content.sha256)) {
            "La descarga local no supera la verificación de integridad"
        }
        val relativePath =
            "blobs/${content.sha256.substring(0, 2)}/${content.sha256.substring(2, 4)}/${content.sha256}"
        val destination = File(context.filesDir, relativePath)
        destination.parentFile?.mkdirs()
        if (destination.exists()) {
            check(verifyHash(destination, content.sha256)) { "Existe un archivo local dañado con el mismo hash" }
            content.file.delete()
        } else {
            check(content.file.renameTo(destination)) { "No se pudo conservar la copia offline" }
        }

        val now = Instant.now().toString()
        val database = writableDatabase
        database.transaction {
            var kind: Int? = null
            database.rawQuery("SELECT kind FROM items WHERE id=?", arrayOf(itemId)).use { cursor ->
                if (cursor.moveToFirst()) kind = cursor.getInt(0)
            }
            require(kind != null) { "El elemento ya no existe" }
            var alreadyAttached = false
            database.rawQuery(
                "SELECT 1 FROM assets WHERE item_id=? AND sha256=? LIMIT 1",
                arrayOf(itemId, content.sha256),
            ).use { cursor -> alreadyAttached = cursor.moveToFirst() }
            if (!alreadyAttached) {
                database.insertOrThrow("assets", null, ContentValues().apply {
                    put("id", UUID.randomUUID().toString())
                    put("item_id", itemId)
                    put("sha256", content.sha256)
                    put("byte_length", content.byteLength)
                    put("mime_type", content.mimeType)
                    put("original_name", content.originalName)
                    put("relative_path", relativePath)
                    put("created_at", now)
                })
            }
            database.update("items", ContentValues().apply {
                if (kind == 1) put("kind", 3)
                put("updated_at", now)
            }, "id=?", arrayOf(itemId))
            content.metadata?.let { applyFetchedMetadata(database, itemId, it, now) }
        }
    }

    fun applyFetchedMetadata(itemId: String, metadata: LinkMetadata): Boolean =
        applyFetchedMetadata(writableDatabase, itemId, metadata, Instant.now().toString())

    private fun applyFetchedMetadata(
        database: SQLiteDatabase,
        itemId: String,
        metadata: LinkMetadata,
        now: String,
    ): Boolean {
        var currentTitle = ""
        var originalUrl: String? = null
        var currentSource: String? = null
        var currentAuthor: String? = null
        var currentDescription: String? = null
        database.rawQuery(
            "SELECT title,original_url,source,author,description FROM items WHERE id=?",
            arrayOf(itemId),
        ).use { cursor ->
            require(cursor.moveToFirst()) { "El elemento ya no existe" }
            currentTitle = cursor.getString(0)
            originalUrl = cursor.getString(1)
            currentSource = cursor.getString(2)
            currentAuthor = cursor.getString(3)
            currentDescription = cursor.getString(4)
        }
        val values = ContentValues()
        val provisionalTitle = originalUrl?.let(SharePayload::titleFromUrl)
        if (!metadata.title.isNullOrBlank() && currentTitle == provisionalTitle) values.put("title", metadata.title)
        if (currentSource.isNullOrBlank() && !metadata.source.isNullOrBlank()) values.put("source", metadata.source)
        if (currentAuthor.isNullOrBlank() && !metadata.author.isNullOrBlank()) values.put("author", metadata.author)
        val resolvedDescription = metadata.resolveDescription(currentDescription)
        if (currentDescription != resolvedDescription) {
            values.putNullable("description", resolvedDescription)
        }
        if (values.size() > 0) {
            values.put("updated_at", now)
            database.update("items", values, "id=?", arrayOf(itemId))
        }
        return values.size() > 0
    }

    fun exportBackup(destination: Uri): AndroidBackupSummary =
        PortableBackupExporter(context).export(readableDatabase, destination)

    fun restoreBackup(source: Uri): AndroidSyncSummary =
        PortableBackupImporter(context).import(writableDatabase, source)

    fun synchronizeDirect(settings: DirectSyncSettings): DirectSyncResult =
        directSyncClient.synchronize(writableDatabase, settings)

    fun directServerRevision(settings: DirectSyncSettings): String =
        directSyncClient.readServerRevision(settings)

    fun hasPendingDirectOperations(): Boolean =
        directSyncClient.hasPendingOperations(readableDatabase)

    fun listSyncConflicts(): List<AndroidSyncConflict> =
        PortableBackupImporter(context).listConflicts(readableDatabase)

    fun resolveSyncConflict(conflictId: String, useRemote: Boolean) =
        PortableBackupImporter(context).resolveConflict(writableDatabase, conflictId, useRemote)

    fun collectOrphanedBlobs(
        nowMillis: Long = System.currentTimeMillis(),
        gracePeriodMillis: Long = ORPHAN_GRACE_PERIOD_MILLIS,
    ): Int {
        require(gracePeriodMillis >= 0) { "El periodo de gracia no puede ser negativo" }
        val referenced = mutableSetOf<String>()
        readableDatabase.rawQuery("SELECT DISTINCT lower(sha256) FROM assets", null).use { cursor ->
            while (cursor.moveToNext()) referenced += cursor.getString(0)
        }
        return BlobGarbageCollector.collect(
            File(context.filesDir, "blobs"), referenced, nowMillis - gracePeriodMillis,
        )
    }

    private fun labelsForItems(
        itemIds: List<String>,
        labelTable: String,
        membershipTable: String,
        labelColumn: String,
    ): Map<String, List<String>> {
        if (itemIds.isEmpty()) return emptyMap()
        val result = mutableMapOf<String, MutableList<String>>()
        itemIds.chunked(400).forEach { chunk ->
            val placeholders = chunk.joinToString(",") { "?" }
            readableDatabase.rawQuery(
                """
                SELECT m.item_id,l.name FROM $membershipTable m JOIN $labelTable l ON l.id=m.$labelColumn
                WHERE m.item_id IN ($placeholders) AND m.removed_at IS NULL
                ORDER BY m.item_id,l.normalized_name
                """.trimIndent(),
                chunk.toTypedArray(),
            ).use { cursor ->
                while (cursor.moveToNext()) {
                    result.getOrPut(cursor.getString(0)) { mutableListOf() } += cursor.getString(1)
                }
            }
        }
        return result
    }

    private fun replaceLabels(
        database: SQLiteDatabase,
        itemId: String,
        labelTable: String,
        membershipTable: String,
        labelColumn: String,
        desired: List<OrganizationName>,
        now: String,
    ) {
        val active = mutableMapOf<String, String>()
        database.rawQuery(
            """
            SELECT l.normalized_name,m.membership_id
            FROM $membershipTable m JOIN $labelTable l ON l.id=m.$labelColumn
            WHERE m.item_id=? AND m.removed_at IS NULL
            """.trimIndent(),
            arrayOf(itemId),
        ).use { cursor -> while (cursor.moveToNext()) active[cursor.getString(0)] = cursor.getString(1) }

        val desiredNames = desired.mapTo(mutableSetOf(), OrganizationName::normalized)
        active.filterKeys { it !in desiredNames }.values.forEach { membershipId ->
            val removed = ContentValues().apply { put("removed_at", now) }
            database.update(membershipTable, removed, "membership_id=?", arrayOf(membershipId))
        }

        desired.filter { it.normalized !in active }.forEach { name ->
            var labelId: String? = null
            database.rawQuery(
                "SELECT id FROM $labelTable WHERE normalized_name=?", arrayOf(name.normalized),
            ).use { cursor -> if (cursor.moveToFirst()) labelId = cursor.getString(0) }
            if (labelId == null) {
                labelId = UUID.randomUUID().toString()
                database.insertOrThrow(labelTable, null, ContentValues().apply {
                    put("id", labelId)
                    put("name", name.display)
                    put("normalized_name", name.normalized)
                })
            }
            database.insertOrThrow(membershipTable, null, ContentValues().apply {
                put("membership_id", UUID.randomUUID().toString())
                put("item_id", itemId)
                put(labelColumn, labelId)
            })
        }
    }

    private fun displayName(uri: Uri): String {
        val fromProvider = context.contentResolver.query(
            uri, arrayOf(OpenableColumns.DISPLAY_NAME), null, null, null,
        )?.use { cursor ->
            if (cursor.moveToFirst()) cursor.getString(0) else null
        }
        val name = (fromProvider ?: uri.lastPathSegment ?: "Archivo")
            .substringAfterLast('/').substringAfterLast('\\').trim()
        return name.ifEmpty { "Archivo" }.take(500)
    }

    private fun verifyHash(file: File, expected: String): Boolean {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().buffered().use { input ->
            val buffer = ByteArray(128 * 1024)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                digest.update(buffer, 0, count)
            }
        }
        return digest.digest().joinToString("") { "%02x".format(it.toInt() and 0xff) } == expected
    }

    private fun itemValues(item: SavedItem, now: String) = ContentValues().apply {
        put("id", item.id)
        put("kind", item.kind)
        put("title", item.title)
        put("original_url", item.originalUrl)
        put("canonical_url", item.originalUrl?.let(SharePayload::canonicalize))
        put("created_at", now)
        put("updated_at", now)
    }

    private fun normalizeTitle(title: String): String {
        val result = title.trim()
        require(result.isNotEmpty() && result.length <= 500) { "Título inválido" }
        return result
    }

    private fun ContentValues.putNullable(name: String, value: String?) {
        val normalized = value?.trim()?.ifEmpty { null }
        if (normalized == null) putNull(name) else put(name, normalized)
    }

    private fun createItemsTable(database: SQLiteDatabase, table: String) {
        database.execSQL(
            """
            CREATE TABLE $table (
                id TEXT PRIMARY KEY NOT NULL,
                kind INTEGER NOT NULL CHECK (kind IN (1, 2, 3)),
                title TEXT NOT NULL,
                original_url TEXT,
                canonical_url TEXT,
                source TEXT,
                author TEXT,
                description TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                deleted_at TEXT,
                favorite INTEGER NOT NULL DEFAULT 0,
                CHECK ((kind = 1 AND original_url IS NOT NULL)
                    OR (kind = 2 AND original_url IS NULL)
                    OR (kind = 3 AND original_url IS NOT NULL))
            )
            """.trimIndent(),
        )
    }

    private fun createAssetsTable(database: SQLiteDatabase) {
        database.execSQL(
            """
            CREATE TABLE assets (
                id TEXT PRIMARY KEY NOT NULL,
                item_id TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE,
                sha256 TEXT NOT NULL,
                byte_length INTEGER NOT NULL,
                mime_type TEXT NOT NULL,
                original_name TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                created_at TEXT
            )
            """.trimIndent(),
        )
        database.execSQL("CREATE INDEX assets_item_id ON assets(item_id)")
    }

    private fun createSyncTables(database: SQLiteDatabase) {
        database.execSQL("CREATE TABLE settings (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL)")
        database.execSQL(
            "INSERT INTO settings(key, value) VALUES('device_id', '${UUID.randomUUID()}')",
        )
        database.execSQL(
            """
            CREATE TABLE sync_item_state (
                peer_device_id TEXT NOT NULL,
                item_id TEXT NOT NULL,
                local_hash TEXT NOT NULL,
                remote_hash TEXT NOT NULL,
                last_synced_at TEXT NOT NULL,
                PRIMARY KEY(peer_device_id, item_id)
            )
            """.trimIndent(),
        )
        database.execSQL(
            """
            CREATE TABLE sync_conflicts (
                id TEXT PRIMARY KEY NOT NULL,
                item_id TEXT NOT NULL,
                peer_device_id TEXT NOT NULL,
                local_json TEXT NOT NULL,
                remote_json TEXT NOT NULL,
                local_hash TEXT NOT NULL,
                remote_hash TEXT NOT NULL,
                created_at TEXT NOT NULL,
                resolved_at TEXT,
                resolution TEXT
            )
            """.trimIndent(),
        )
        database.execSQL(
            "CREATE INDEX sync_conflicts_unresolved ON sync_conflicts(resolved_at, item_id)",
        )
    }

    private fun createOrganizationTables(database: SQLiteDatabase) {
        database.execSQL(
            "CREATE TABLE tags (id TEXT PRIMARY KEY NOT NULL, name TEXT NOT NULL, normalized_name TEXT NOT NULL UNIQUE)",
        )
        database.execSQL(
            "CREATE TABLE collections (id TEXT PRIMARY KEY NOT NULL, name TEXT NOT NULL, normalized_name TEXT NOT NULL UNIQUE)",
        )
        database.execSQL(
            """
            CREATE TABLE item_tags (
                membership_id TEXT PRIMARY KEY NOT NULL,
                item_id TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE,
                tag_id TEXT NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
                removed_at TEXT
            )
            """.trimIndent(),
        )
        database.execSQL(
            """
            CREATE TABLE item_collections (
                membership_id TEXT PRIMARY KEY NOT NULL,
                item_id TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE,
                collection_id TEXT NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
                removed_at TEXT
            )
            """.trimIndent(),
        )
    }

    private fun createOperationTracking(database: SQLiteDatabase, seedExistingItems: Boolean) {
        database.execSQL(
            "INSERT OR IGNORE INTO settings(key, value) VALUES('sync_importing', '0')",
        )
        database.execSQL(
            """
            CREATE TABLE sync_operations (
                counter INTEGER PRIMARY KEY AUTOINCREMENT,
                item_id TEXT NOT NULL,
                created_at TEXT NOT NULL
            )
            """.trimIndent(),
        )
        database.execSQL("CREATE INDEX sync_operations_item ON sync_operations(item_id, counter)")
        if (seedExistingItems) {
            database.execSQL(
                """
                INSERT INTO sync_operations(item_id, created_at)
                SELECT id, strftime('%Y-%m-%dT%H:%M:%fZ', 'now') FROM items ORDER BY id
                """.trimIndent(),
            )
        }
        database.execSQL(
            """
            CREATE TRIGGER sync_items_insert AFTER INSERT ON items
            WHEN COALESCE((SELECT value FROM settings WHERE key='sync_importing'), '0') <> '1'
            BEGIN
                INSERT INTO sync_operations(item_id, created_at)
                VALUES(NEW.id, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            END
            """.trimIndent(),
        )
        database.execSQL(
            """
            CREATE TRIGGER sync_items_update AFTER UPDATE ON items
            WHEN COALESCE((SELECT value FROM settings WHERE key='sync_importing'), '0') <> '1'
            BEGIN
                INSERT INTO sync_operations(item_id, created_at)
                VALUES(NEW.id, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
            END
            """.trimIndent(),
        )
    }

    companion object {
        private const val MAXIMUM_FILE_BYTES = 1024L * 1024 * 1024
        private const val ORPHAN_GRACE_PERIOD_MILLIS = 7L * 24 * 60 * 60 * 1000
    }
}
