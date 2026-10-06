package org.unishare.app

import android.annotation.SuppressLint
import android.content.Context
import android.database.sqlite.SQLiteDatabase
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.net.Uri
import androidx.work.BackoffPolicy
import androidx.work.Constraints
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.Worker
import androidx.work.WorkerParameters
import androidx.work.workDataOf
import androidx.core.content.edit
import java.io.File
import java.io.FileOutputStream
import java.net.HttpURLConnection
import java.net.URI
import java.net.URL
import java.net.URLDecoder
import java.net.UnknownHostException
import java.time.Instant
import java.util.UUID
import java.util.concurrent.TimeUnit
import org.json.JSONArray
import org.json.JSONObject

data class DirectSyncSettings(val endpoint: String, val pairingKey: String)

object DirectSyncPairing {
    fun parse(link: String): DirectSyncSettings {
        val uri = URI(link)
        require(uri.scheme.equals("unishare", ignoreCase = true) &&
            uri.host.equals("pair", ignoreCase = true)) {
            "El QR no pertenece a UniShare"
        }
        val values = uri.rawQuery.orEmpty().split('&').filter { it.isNotBlank() }.associate { field ->
            val parts = field.split('=', limit = 2)
            require(parts.size == 2) { "El enlace de emparejamiento está incompleto" }
            URLDecoder.decode(parts[0], Charsets.UTF_8.name()) to
                URLDecoder.decode(parts[1], Charsets.UTF_8.name())
        }
        val endpoint = values["endpoint"]?.trim()?.trimEnd('/')
        val key = values["key"]?.trim()
        require(!endpoint.isNullOrBlank() && !key.isNullOrBlank()) {
            "El enlace de emparejamiento está incompleto"
        }
        val publicEndpoint = URI(endpoint)
        val emulatorLoopback = BuildConfig.DEBUG &&
            publicEndpoint.scheme.equals("http", ignoreCase = true) &&
            publicEndpoint.host in setOf("10.0.2.2", "127.0.0.1")
        require(emulatorLoopback || (publicEndpoint.scheme.equals("https", ignoreCase = true) &&
            !publicEndpoint.host.isNullOrBlank() &&
            publicEndpoint.host.endsWith(".ts.net", ignoreCase = true))) {
            "El QR no contiene una dirección privada válida de Tailscale"
        }
        require(key.length >= 32) { "La clave de emparejamiento no es válida" }
        return DirectSyncSettings(endpoint, key)
    }
}

data class DirectSyncResult(
    val sentItems: Int,
    val receivedItems: Int,
    val updatedItems: Int,
    val conflicts: Int,
    val serverRevision: String,
)

data class DirectSyncServerStatus(
    val revision: String,
    val protocolVersion: Int,
    val deviceId: String?,
    val cursor: Long?,
)

data class DirectSyncCursors(val serverCursor: Long?, val acknowledgedLocalCounter: Long?)

object DirectSyncPolicy {
    fun isUpToDate(
        serverRevision: String?,
        lastRevision: String?,
        localChangesPending: Boolean,
    ): Boolean = lastRevision != null &&
        serverRevision == lastRevision &&
        !localChangesPending

    fun shouldTransfer(
        force: Boolean,
        serverRevision: String?,
        lastRevision: String?,
        localChangesPending: Boolean,
    ): Boolean = force || !isUpToDate(serverRevision, lastRevision, localChangesPending)
}

object DirectSyncCursorPolicy {
    fun hasPending(through: Long, acknowledged: Long?): Boolean =
        through > (acknowledged ?: 0L)

    fun requiresFullUpload(
        supportsDelta: Boolean,
        through: Long,
        acknowledged: Long?,
    ): Boolean = !supportsDelta || acknowledged == null || acknowledged > through

    fun responseIsCausallyValid(
        expectedDeviceId: String,
        responseDeviceId: String?,
        previousServerCursor: Long?,
        serverThrough: Long?,
        expectedClientAck: Long,
        clientAck: Long?,
        fullSnapshot: Boolean,
    ): Boolean = responseDeviceId == expectedDeviceId && serverThrough != null &&
        (fullSnapshot || serverThrough >= (previousServerCursor ?: 0L)) &&
        clientAck == expectedClientAck
}

object DirectSyncErrorMessages {
    fun forUser(error: Throwable): String {
        val unknownHost = generateSequence(error as Throwable?) { it.cause }
            .any { it is UnknownHostException }
        return if (unknownHost) {
            "Tailscale no puede resolver el ordenador. Abre Tailscale, activa «Usar DNS de Tailscale» " +
                "y comprueba que UniShare no esté excluida del túnel dividido."
        } else {
            error.message ?: "El ordenador no está disponible."
        }
    }
}

object DirectSyncPreferences {
    private const val NAME = "direct_sync"
    private const val ENDPOINT = "endpoint"
    private const val KEY = "pairing_key"
    private const val LAST_RESULT = "last_result"
    private const val LAST_REVISION = "last_server_revision"
    private const val LOCAL_GENERATION = "local_generation"
    private const val SYNCED_LOCAL_GENERATION = "synced_local_generation"
    private const val SERVER_DEVICE_ID = "server_device_id"
    private const val LOCAL_DEVICE_ID = "local_device_id"
    private const val SERVER_CURSOR = "server_cursor"
    private const val ACKNOWLEDGED_LOCAL_COUNTER = "acknowledged_local_counter"

    fun load(context: Context): DirectSyncSettings? {
        val preferences = context.getSharedPreferences(NAME, Context.MODE_PRIVATE)
        val endpoint = preferences.getString(ENDPOINT, null)?.trim().orEmpty()
        val key = preferences.getString(KEY, null)?.trim().orEmpty()
        return if (endpoint.isEmpty() || key.isEmpty()) null else DirectSyncSettings(endpoint, key)
    }

    fun save(context: Context, rawEndpoint: String, rawKey: String): DirectSyncSettings {
        val endpoint = rawEndpoint.trim().trimEnd('/')
        val key = rawKey.trim()
        val uri = URI(endpoint)
        val emulatorLoopback = BuildConfig.DEBUG && uri.scheme.equals("http", ignoreCase = true) &&
            uri.host in setOf("10.0.2.2", "127.0.0.1")
        require((uri.scheme.equals("https", ignoreCase = true) || emulatorLoopback) &&
            !uri.host.isNullOrBlank()) {
            "Usa la dirección HTTPS que muestra Tailscale Serve"
        }
        require(key.length >= 32) { "La clave de emparejamiento no es válida" }
        val preferences = context.getSharedPreferences(NAME, Context.MODE_PRIVATE)
        val changedPeer = preferences.getString(ENDPOINT, null) != endpoint ||
            preferences.getString(KEY, null) != key
        preferences.edit {
            putString(ENDPOINT, endpoint)
            putString(KEY, key)
            if (changedPeer) {
                remove(SERVER_DEVICE_ID)
                remove(LOCAL_DEVICE_ID)
                remove(SERVER_CURSOR)
                remove(ACKNOWLEDGED_LOCAL_COUNTER)
                remove(LAST_REVISION)
            }
        }
        return DirectSyncSettings(endpoint, key)
    }

    fun writeLastResult(context: Context, message: String) {
        context.getSharedPreferences(NAME, Context.MODE_PRIVATE).edit {
            putString(LAST_RESULT, "${Instant.now()}: $message")
        }
    }

    fun lastResult(context: Context): String? =
        context.getSharedPreferences(NAME, Context.MODE_PRIVATE).getString(LAST_RESULT, null)

    fun lastRevision(context: Context): String? =
        context.getSharedPreferences(NAME, Context.MODE_PRIVATE).getString(LAST_REVISION, null)

    fun writeLastRevision(context: Context, revision: String) {
        if (revision.isBlank()) return
        context.getSharedPreferences(NAME, Context.MODE_PRIVATE).edit {
            putString(LAST_REVISION, revision)
        }
    }

    fun markLocalChangePending(context: Context) {
        val preferences = context.getSharedPreferences(NAME, Context.MODE_PRIVATE)
        val next = preferences.getLong(LOCAL_GENERATION, 0L) + 1L
        // SQLite's operation log is the durable source of truth. This preference only refreshes
        // the visible state immediately and therefore must not turn an already committed mutation
        // into an apparent failure if SharedPreferences cannot flush synchronously.
        preferences.edit { putLong(LOCAL_GENERATION, next) }
    }

    fun localGeneration(context: Context): Long =
        context.getSharedPreferences(NAME, Context.MODE_PRIVATE).getLong(LOCAL_GENERATION, 0L)

    fun localChangesPending(context: Context): Boolean {
        val preferences = context.getSharedPreferences(NAME, Context.MODE_PRIVATE)
        return preferences.getLong(LOCAL_GENERATION, 0L) >
            preferences.getLong(SYNCED_LOCAL_GENERATION, 0L)
    }

    fun markLocalGenerationSynced(context: Context, generation: Long) {
        val preferences = context.getSharedPreferences(NAME, Context.MODE_PRIVATE)
        val current = preferences.getLong(SYNCED_LOCAL_GENERATION, 0L)
        if (generation > current) {
            preferences.edit { putLong(SYNCED_LOCAL_GENERATION, generation) }
        }
    }

    @SuppressLint("UseKtx") // commit() result is required before acknowledging a causal cursor.
    fun prepareCursors(
        context: Context,
        serverDeviceId: String,
        localDeviceId: String,
    ): DirectSyncCursors {
        val preferences = context.getSharedPreferences(NAME, Context.MODE_PRIVATE)
        val identityChanged = preferences.getString(SERVER_DEVICE_ID, null) != serverDeviceId ||
            preferences.getString(LOCAL_DEVICE_ID, null) != localDeviceId
        if (identityChanged) {
            check(preferences.edit()
                .putString(SERVER_DEVICE_ID, serverDeviceId)
                .putString(LOCAL_DEVICE_ID, localDeviceId)
                .remove(SERVER_CURSOR)
                .remove(ACKNOWLEDGED_LOCAL_COUNTER)
                .commit()) {
                "No se pudo reiniciar el estado incremental"
            }
            return DirectSyncCursors(null, null)
        }
        return DirectSyncCursors(
            preferences.longOrNull(SERVER_CURSOR),
            preferences.longOrNull(ACKNOWLEDGED_LOCAL_COUNTER),
        )
    }

    @SuppressLint("UseKtx") // commit() result is required before pruning acknowledged operations.
    fun commitCursors(
        context: Context,
        serverDeviceId: String,
        localDeviceId: String,
        serverThrough: Long,
        acknowledgedLocalCounter: Long,
    ) {
        require(serverThrough >= 0 && acknowledgedLocalCounter >= 0) { "Cursor de sincronización inválido" }
        check(context.getSharedPreferences(NAME, Context.MODE_PRIVATE).edit()
            .putString(SERVER_DEVICE_ID, serverDeviceId)
            .putString(LOCAL_DEVICE_ID, localDeviceId)
            .putLong(SERVER_CURSOR, serverThrough)
            .putLong(ACKNOWLEDGED_LOCAL_COUNTER, acknowledgedLocalCounter)
            .commit()) {
            "No se pudo guardar el acuse de sincronización"
        }
    }

    fun acknowledgedLocalCounter(context: Context, localDeviceId: String): Long? {
        val preferences = context.getSharedPreferences(NAME, Context.MODE_PRIVATE)
        if (preferences.getString(LOCAL_DEVICE_ID, null) != localDeviceId) return null
        return preferences.longOrNull(ACKNOWLEDGED_LOCAL_COUNTER)
    }

    private fun android.content.SharedPreferences.longOrNull(key: String): Long? =
        if (contains(key)) getLong(key, 0L) else null
}

class DirectSyncClient(private val context: Context) {
    private val connectivityManager = context.getSystemService(ConnectivityManager::class.java)

    fun synchronize(database: SQLiteDatabase, settings: DirectSyncSettings): DirectSyncResult =
        synchronized(processLock) {
            val request = File(context.cacheDir, "direct-request-${UUID.randomUUID()}.zip")
            val response = File(context.cacheDir, "direct-response-${UUID.randomUUID()}.zip")
            try {
                val status = readServerStatus(settings)
                val localDeviceId = readLocalDeviceId(database)
                val supportsDelta = status.protocolVersion >= 2 &&
                    !status.deviceId.isNullOrBlank() && status.cursor != null
                val cursors = if (supportsDelta) {
                    DirectSyncPreferences.prepareCursors(context, status.deviceId, localDeviceId)
                } else {
                    DirectSyncCursors(null, null)
                }
                val delta = readDeltaPlan(database, cursors.acknowledgedLocalCounter, supportsDelta)
                val availableBlobs = readAvailableBlobHashes(database)
                val missingOnServer = readMissingBlobHashes(settings, availableBlobs)
                val outgoing = PortableBackupExporter(context).export(
                    database,
                    Uri.fromFile(request),
                    includedBlobHashes = missingOnServer,
                    availableBlobHashes = missingOnServer?.let { availableBlobs },
                    includedItemIds = delta.itemIds,
                )
                val connection = openConnection(URL("${settings.endpoint}/api/v1/sync")).apply {
                    requestMethod = "POST"
                    connectTimeout = 15_000
                    readTimeout = 5 * 60_000
                    doOutput = true
                    instanceFollowRedirects = false
                    setRequestProperty("Authorization", "Bearer ${settings.pairingKey}")
                    setRequestProperty("Content-Type", "application/vnd.unishare.sync+zip")
                    if (supportsDelta) {
                        setRequestProperty("X-UniShare-Delta", "1")
                        setRequestProperty("X-UniShare-Client-Through", delta.through.toString())
                        cursors.serverCursor?.let {
                            setRequestProperty("X-UniShare-Server-Cursor", it.toString())
                        }
                    }
                    setFixedLengthStreamingMode(request.length())
                }
                var serverRevision = ""
                var responseProtocol = 0
                var responseServerDevice: String? = null
                var responseServerThrough: Long? = null
                var responseClientAck: Long? = null
                var responseIsFullSnapshot = false
                try {
                    connection.outputStream.use { output -> request.inputStream().buffered().use { it.copyTo(output) } }
                    val code = connection.responseCode
                    if (code != HttpURLConnection.HTTP_OK) {
                        val detail = connection.errorStream?.bufferedReader()?.use { it.readText().take(2_000) }
                        throw IllegalStateException(
                            if (detail.isNullOrBlank()) "El ordenador respondió con HTTP $code" else detail,
                        )
                    }
                    serverRevision = connection.getHeaderField("X-UniShare-Revision").orEmpty()
                    responseProtocol = connection.getHeaderField("X-UniShare-Protocol")?.toIntOrNull() ?: 0
                    responseServerDevice = connection.getHeaderField("X-UniShare-Server-Device")
                    responseServerThrough = connection.getHeaderField("X-UniShare-Server-Through")?.toLongOrNull()
                    responseClientAck = connection.getHeaderField("X-UniShare-Client-Ack")?.toLongOrNull()
                    responseIsFullSnapshot = connection.getHeaderField("X-UniShare-Full-Snapshot") == "1"
                    FileOutputStream(response).use { fileOutput ->
                        connection.inputStream.use { input ->
                            val buffer = ByteArray(128 * 1024)
                            var total = 0L
                            while (true) {
                                val count = input.read(buffer)
                                if (count < 0) break
                                total += count
                                require(total <= MAXIMUM_RESPONSE_BYTES) { "La respuesta supera 4 GiB" }
                                fileOutput.write(buffer, 0, count)
                            }
                        }
                        fileOutput.fd.sync()
                    }
                } finally {
                    connection.disconnect()
                }
                val incoming = PortableBackupImporter(context).import(
                    database, Uri.fromFile(response), trackAsLocalChanges = false,
                )
                if (supportsDelta) {
                    check(responseProtocol >= 2) { "El servidor no confirmó el protocolo incremental" }
                    check(DirectSyncCursorPolicy.responseIsCausallyValid(
                        status.deviceId,
                        responseServerDevice,
                        cursors.serverCursor,
                        responseServerThrough,
                        delta.through,
                        responseClientAck,
                        responseIsFullSnapshot,
                    )) { "El ordenador no confirmó causalmente la sincronización" }
                    val serverThrough = checkNotNull(responseServerThrough)
                    DirectSyncPreferences.commitCursors(
                        context, status.deviceId, localDeviceId, serverThrough, delta.through,
                    )
                    database.delete(
                        "sync_operations", "counter<=?", arrayOf(delta.through.toString()),
                    )
                }
                DirectSyncResult(
                    outgoing.itemCount,
                    incoming.importedItems,
                    incoming.updatedItems,
                    incoming.conflicts,
                    serverRevision,
                )
            } finally {
                request.delete()
                response.delete()
            }
        }

    fun hasPendingOperations(database: SQLiteDatabase): Boolean {
        val localDeviceId = readLocalDeviceId(database)
        val acknowledged = DirectSyncPreferences.acknowledgedLocalCounter(context, localDeviceId)
        val through = readOperationThrough(database)
        return DirectSyncCursorPolicy.hasPending(through, acknowledged)
    }

    private fun readMissingBlobHashes(
        settings: DirectSyncSettings,
        availableBlobs: Set<String>,
    ): Set<String>? {
        val body = JSONObject()
            .put("hashes", JSONArray(availableBlobs.sorted()))
            .toString()
            .toByteArray(Charsets.UTF_8)
        val connection = openConnection(URL("${settings.endpoint}/api/v1/blobs/missing")).apply {
            requestMethod = "POST"
            connectTimeout = 15_000
            readTimeout = 30_000
            doOutput = true
            instanceFollowRedirects = false
            setRequestProperty("Authorization", "Bearer ${settings.pairingKey}")
            setRequestProperty("Content-Type", "application/json; charset=utf-8")
            setFixedLengthStreamingMode(body.size)
        }
        return try {
            connection.outputStream.use { it.write(body) }
            when (val code = connection.responseCode) {
                HttpURLConnection.HTTP_NOT_FOUND, HttpURLConnection.HTTP_BAD_METHOD -> null
                HttpURLConnection.HTTP_OK -> {
                    val payload = connection.inputStream.bufferedReader().use { it.readText() }
                    val values = JSONObject(payload).getJSONArray("missing")
                    buildSet {
                        repeat(values.length()) {
                            val hash = values.getString(it)
                            require(hash in availableBlobs) { "El servidor devolvió un blob ajeno al inventario" }
                            add(hash)
                        }
                    }
                }
                else -> {
                    val detail = connection.errorStream?.bufferedReader()?.use { it.readText().take(2_000) }
                    throw IllegalStateException(
                        if (detail.isNullOrBlank()) "El ordenador respondió con HTTP $code" else detail,
                    )
                }
            }
        } finally {
            connection.disconnect()
        }
    }

    private fun readAvailableBlobHashes(database: SQLiteDatabase): Set<String> = buildSet {
        database.rawQuery(
            "SELECT DISTINCT lower(sha256),relative_path,byte_length FROM assets ORDER BY lower(sha256)", null,
        ).use { cursor ->
            while (cursor.moveToNext()) {
                val hash = cursor.getString(0)
                require(HASH_PATTERN.matches(hash)) { "La biblioteca contiene un SHA-256 inválido" }
                val expectedPath = "blobs/${hash.substring(0, 2)}/${hash.substring(2, 4)}/$hash"
                require(cursor.getString(1) == expectedPath) { "La biblioteca contiene una ruta de blob inválida" }
                val file = File(context.filesDir, expectedPath)
                if (file.isFile && file.length() == cursor.getLong(2)) add(hash)
            }
        }
    }

    fun readServerRevision(settings: DirectSyncSettings): String = readServerStatus(settings).revision

    private fun readServerStatus(settings: DirectSyncSettings): DirectSyncServerStatus {
        val connection = openConnection(URL("${settings.endpoint}/api/v1/status")).apply {
            requestMethod = "GET"
            connectTimeout = 10_000
            readTimeout = 10_000
            instanceFollowRedirects = false
            setRequestProperty("Authorization", "Bearer ${settings.pairingKey}")
        }
        return try {
            val code = connection.responseCode
            check(code == HttpURLConnection.HTTP_OK) { "El ordenador respondió con HTTP $code" }
            val body = connection.inputStream.bufferedReader().use { it.readText() }
            val json = JSONObject(body)
            DirectSyncServerStatus(
                json.getString("revision"),
                json.optInt("protocol_version", 1),
                json.optString("device_id").ifBlank { null },
                if (json.has("cursor") && !json.isNull("cursor")) json.getLong("cursor") else null,
            )
        } finally {
            connection.disconnect()
        }
    }

    private fun readLocalDeviceId(database: SQLiteDatabase): String =
        database.rawQuery("SELECT value FROM settings WHERE key='device_id'", null).use { cursor ->
            check(cursor.moveToFirst()) { "Falta la identidad del teléfono" }
            UUID.fromString(cursor.getString(0)).toString()
        }

    private fun readDeltaPlan(
        database: SQLiteDatabase,
        acknowledgedCounter: Long?,
        supportsDelta: Boolean,
    ): DeltaPlan {
        val through = readOperationThrough(database)
        if (DirectSyncCursorPolicy.requiresFullUpload(supportsDelta, through, acknowledgedCounter)) {
            return DeltaPlan(through, null)
        }
        val itemIds = buildSet {
            database.rawQuery(
                "SELECT DISTINCT item_id FROM sync_operations WHERE counter>? AND counter<=? ORDER BY item_id",
                arrayOf(acknowledgedCounter.toString(), through.toString()),
            ).use { cursor -> while (cursor.moveToNext()) add(cursor.getString(0)) }
        }
        return DeltaPlan(through, itemIds)
    }

    private fun readOperationThrough(database: SQLiteDatabase): Long = database.rawQuery(
        "SELECT COALESCE((SELECT seq FROM sqlite_sequence WHERE name='sync_operations'), 0)", null,
    ).use { cursor ->
        check(cursor.moveToFirst()) { "No se pudo leer el registro de operaciones" }
        cursor.getLong(0)
    }

    companion object {
        private val processLock = Any()
        private const val MAXIMUM_RESPONSE_BYTES = 4L * 1024 * 1024 * 1024
        private val HASH_PATTERN = Regex("^[0-9a-f]{64}$")
    }

    private data class DeltaPlan(val through: Long, val itemIds: Set<String>?)

    private fun openConnection(url: URL): HttpURLConnection {
        @Suppress("DEPRECATION")
        val vpnNetwork = connectivityManager.allNetworks.firstOrNull { network ->
            connectivityManager.getNetworkCapabilities(network)
                ?.hasTransport(NetworkCapabilities.TRANSPORT_VPN) == true
        }
        if (url.host.endsWith(".ts.net", ignoreCase = true) && vpnNetwork == null) {
            throw IllegalStateException(
                "Tailscale no está conectado. Abre Tailscale, activa la conexión y vuelve a intentarlo.",
            )
        }
        return ((vpnNetwork?.openConnection(url) ?: url.openConnection()) as HttpURLConnection)
    }
}

object DirectSyncCoordinator {
    fun markVerifiedUpToDate(context: Context) {
        DirectSyncPreferences.writeLastResult(context, "correcto (sin cambios)")
    }

    fun synchronize(
        context: Context,
        store: LocalLibraryStore,
        settings: DirectSyncSettings,
    ): DirectSyncResult = try {
        val generationAtStart = DirectSyncPreferences.localGeneration(context)
        store.synchronizeDirect(settings).also { result ->
            DirectSyncPreferences.writeLastRevision(context, result.serverRevision)
            DirectSyncPreferences.markLocalGenerationSynced(context, generationAtStart)
            DirectSyncPreferences.writeLastResult(
                context,
                "correcto (${result.receivedItems} nuevos, ${result.updatedItems} actualizados)",
            )
        }
    } catch (error: Exception) {
        val message = DirectSyncErrorMessages.forUser(error)
        DirectSyncPreferences.writeLastResult(context, "pendiente: $message")
        throw IllegalStateException(message, error)
    }
}

object DirectSyncScheduler {
    private const val PERIODIC_NAME = "unishare-direct-sync-periodic"
    private const val IMMEDIATE_NAME = "unishare-direct-sync-immediate"

    private val connected = Constraints.Builder()
        .setRequiredNetworkType(NetworkType.CONNECTED)
        .build()

    fun schedule(context: Context) {
        if (DirectSyncPreferences.load(context) == null) return
        val work = PeriodicWorkRequestBuilder<DirectSyncWorker>(15, TimeUnit.MINUTES)
            .setConstraints(connected)
            .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 30, TimeUnit.SECONDS)
            .build()
        WorkManager.getInstance(context).enqueueUniquePeriodicWork(
            PERIODIC_NAME,
            ExistingPeriodicWorkPolicy.UPDATE,
            work,
        )
    }

    fun enqueueNow(context: Context) {
        if (DirectSyncPreferences.load(context) == null) return
        val work = OneTimeWorkRequestBuilder<DirectSyncWorker>()
            .setConstraints(connected)
            .setInputData(workDataOf(DirectSyncWorker.FORCE_TRANSFER to true))
            .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 10, TimeUnit.SECONDS)
            .build()
        WorkManager.getInstance(context).enqueueUniqueWork(
            IMMEDIATE_NAME,
            ExistingWorkPolicy.REPLACE,
            work,
        )
    }
}

class DirectSyncWorker(context: Context, parameters: WorkerParameters) : Worker(context, parameters) {
    override fun doWork(): Result {
        val settings = DirectSyncPreferences.load(applicationContext) ?: return Result.success()
        val store = LocalLibraryStore(applicationContext)
        return try {
            val force = inputData.getBoolean(FORCE_TRANSFER, false)
            if (!force) {
                val serverRevision = store.directServerRevision(settings)
                if (!DirectSyncPolicy.shouldTransfer(
                        force = false,
                        serverRevision,
                        DirectSyncPreferences.lastRevision(applicationContext),
                        DirectSyncPreferences.localChangesPending(applicationContext) ||
                            store.hasPendingDirectOperations(),
                    )
                ) {
                    DirectSyncCoordinator.markVerifiedUpToDate(applicationContext)
                    return Result.success()
                }
            }
            DirectSyncCoordinator.synchronize(applicationContext, store, settings)
            Result.success()
        } catch (_: Exception) {
            Result.retry()
        } finally {
            store.close()
        }
    }

    companion object {
        const val FORCE_TRANSFER = "force_transfer"
    }
}
