package org.unishare.app

import android.net.Uri
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.io.ByteArrayOutputStream
import java.io.File
import java.net.InetAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.SocketException
import java.util.UUID
import java.util.concurrent.atomic.AtomicReference
import kotlin.concurrent.thread

@RunWith(AndroidJUnit4::class)
class DirectSyncRuntimeTest {
    private val base = InstrumentationRegistry.getInstrumentation().targetContext

    @Test
    fun interruptedResponseKeepsTheOutboxAndRetryConvergesWithoutDuplicates() {
        IsolatedLibraryContext(base).use { phone ->
            IsolatedLibraryContext(base).use { desktop ->
                LocalLibraryStore(phone).use { local ->
                    LocalLibraryStore(desktop).use { remote ->
                        val localItem = local.saveLink("Teléfono", "https://example.invalid/phone")
                        val remoteItem = remote.saveLink("Ordenador", "https://example.invalid/desktop")
                        val bytes = ByteArray(262_144).also { java.util.Random(31).nextBytes(it) }
                        AndroidPersistenceTest.attach(remote, desktop, remoteItem.id, bytes)
                        SyncTestPeer(desktop, remote).use { peer ->
                            val settings = DirectSyncPreferences.save(phone, peer.endpoint, peer.key)
                            peer.truncateNextResponse = true
                            assertTrue(runCatching { local.synchronizeDirect(settings) }.isFailure)
                            assertTrue(local.hasPendingDirectOperations())
                            assertEquals(listOf(localItem.id), local.listItems().map { it.id })
                            assertEquals(2, remote.listItems().size)
                            assertNoTransferPartials(phone)

                            val recovered = local.synchronizeDirect(settings)
                            assertEquals(0, recovered.conflicts)
                            assertEquals(setOf(localItem.id, remoteItem.id), local.listItems().map { it.id }.toSet())
                            assertEquals(2, remote.listItems().size)
                            assertFalse(local.hasPendingDirectOperations())
                            assertArrayEquals(bytes, local.localAsset(remoteItem.id)!!.file.readBytes())
                            assertNoTransferPartials(phone)
                            local.synchronizeDirect(settings)
                            assertEquals(2, local.listItems().size)
                            assertTrue(local.listSyncConflicts().isEmpty())
                            peer.assertHealthy()
                        }
                    }
                }
            }
        }
    }

    @Test
    fun invalidAcknowledgementCannotImportAValidRemotePackage() {
        IsolatedLibraryContext(base).use { phone ->
            IsolatedLibraryContext(base).use { desktop ->
                LocalLibraryStore(phone).use { local ->
                    LocalLibraryStore(desktop).use { remote ->
                        local.saveLink("Local", "https://example.invalid/local")
                        remote.saveLink("Remoto", "https://example.invalid/remote")
                        SyncTestPeer(desktop, remote).use { peer ->
                            val settings = DirectSyncPreferences.save(phone, peer.endpoint, peer.key)
                            peer.invalidNextAcknowledgement = true
                            assertTrue(runCatching { local.synchronizeDirect(settings) }.isFailure)
                            assertEquals(listOf("Local"), local.listItems().map { it.title })
                            assertTrue(local.hasPendingDirectOperations())
                            local.synchronizeDirect(settings)
                            assertEquals(setOf("Local", "Remoto"), local.listItems().map { it.title }.toSet())
                            assertFalse(local.hasPendingDirectOperations())
                            peer.assertHealthy()
                        }
                    }
                }
            }
        }
    }

    @Test
    fun sameLengthCorruptionIsRepairedFromTheOtherReplica() {
        IsolatedLibraryContext(base).use { phone ->
            IsolatedLibraryContext(base).use { desktop ->
                LocalLibraryStore(phone).use { local ->
                    LocalLibraryStore(desktop).use { remote ->
                        val item = remote.saveLink("Archivo", "https://example.invalid/repair")
                        val bytes = ByteArray(4096) { (it % 251).toByte() }
                        AndroidPersistenceTest.attach(remote, desktop, item.id, bytes)
                        SyncTestPeer(desktop, remote).use { peer ->
                            val settings = DirectSyncPreferences.save(phone, peer.endpoint, peer.key)
                            local.synchronizeDirect(settings)
                            local.localAsset(item.id)!!.file.writeBytes(ByteArray(bytes.size))
                            local.synchronizeDirect(settings)
                            assertArrayEquals(bytes, local.localAsset(item.id)!!.file.readBytes())
                            assertArrayEquals(ByteArray(bytes.size),
                                File(phone.filesDir, "recovery/corrupt-blobs").listFiles()!!.single().readBytes())
                            assertEquals(1, local.listItems().size)
                            peer.assertHealthy()
                        }
                    }
                }
            }
        }
    }

    private fun assertNoTransferPartials(context: IsolatedLibraryContext) {
        assertTrue(context.cacheDir.listFiles().orEmpty().none {
            it.name.startsWith("direct-request-") || it.name.startsWith("direct-response-")
        })
    }
}

/** A real HTTP socket plus the real portable importer/exporter, with controllable network failures. */
private class SyncTestPeer(
    private val context: IsolatedLibraryContext,
    private val store: LocalLibraryStore,
) : AutoCloseable {
    private val socket = ServerSocket(0, 10, InetAddress.getByName("127.0.0.1"))
    private val error = AtomicReference<Throwable?>()
    private val deviceId = store.readableDatabase.rawQuery(
        "SELECT value FROM settings WHERE key='device_id'", null,
    ).use { cursor -> check(cursor.moveToFirst()); cursor.getString(0) }
    val key = UUID.randomUUID().toString() + UUID.randomUUID()
    val endpoint = "http://127.0.0.1:${socket.localPort}"
    @Volatile var truncateNextResponse = false
    @Volatile var invalidNextAcknowledgement = false
    private val worker = thread(name = "UniShare test peer", isDaemon = true) {
        try {
            while (!socket.isClosed) socket.accept().use(::respond)
        } catch (failure: Throwable) {
            if (!(failure is SocketException && socket.isClosed)) error.set(failure)
        }
    }

    private fun respond(client: Socket) {
        client.soTimeout = 10_000
        val input = client.getInputStream().buffered()
        val header = ByteArrayOutputStream()
        var tail = 0
        while (tail != 0x0d0a0d0a) {
            val next = input.read()
            check(next >= 0 && header.size() < 65_536) { "Invalid HTTP request" }
            header.write(next)
            tail = (tail shl 8) or next
        }
        val lines = header.toString("UTF-8").split("\r\n")
        val path = lines.first().split(' ')[1]
        val headers = lines.drop(1).filter { it.contains(':') }.associate {
            it.substringBefore(':').lowercase() to it.substringAfter(':').trim()
        }
        check(headers["authorization"] == "Bearer $key") { "Missing test authentication" }
        val length = headers["content-length"]?.toInt() ?: 0
        check(length in 0..2_000_000)
        val body = ByteArray(length)
        var received = 0
        while (received < length) {
            val count = input.read(body, received, length - received)
            check(count > 0) { "Truncated request" }
            received += count
        }

        val extraHeaders = mutableMapOf<String, String>()
        val response = when (path) {
            "/api/v1/status" -> JSONObject().put("protocol_version", 2).put("revision", "test-revision")
                .put("device_id", deviceId).put("cursor", 1).toString().toByteArray()
            "/api/v1/blobs/missing" -> {
                val hashes = JSONObject(String(body, Charsets.UTF_8)).getJSONArray("hashes")
                val missing = JSONArray()
                repeat(hashes.length()) { index ->
                    val hash = hashes.getString(index)
                    if (!File(context.filesDir, "blobs/${hash.take(2)}/${hash.substring(2, 4)}/$hash").isFile) {
                        missing.put(hash)
                    }
                }
                JSONObject().put("missing", missing).toString().toByteArray()
            }
            "/api/v1/sync" -> {
                val request = File(context.cacheDir, "peer-request.zip").apply { writeBytes(body) }
                val response = File(context.cacheDir, "peer-response.zip")
                try {
                    PortableBackupImporter(context).import(store.writableDatabase, Uri.fromFile(request), false)
                    store.exportBackup(Uri.fromFile(response))
                    extraHeaders["X-UniShare-Revision"] = "test-revision"
                    extraHeaders["X-UniShare-Protocol"] = "2"
                    extraHeaders["X-UniShare-Server-Device"] = deviceId
                    extraHeaders["X-UniShare-Server-Through"] = "1"
                    val through = checkNotNull(headers["x-unishare-client-through"]).toLong()
                    extraHeaders["X-UniShare-Client-Ack"] =
                        (if (invalidNextAcknowledgement) through + 1 else through).toString()
                    invalidNextAcknowledgement = false
                    extraHeaders["X-UniShare-Full-Snapshot"] = "1"
                    response.readBytes()
                } finally {
                    request.delete()
                    response.delete()
                }
            }
            else -> error("Unexpected test route $path")
        }
        client.getOutputStream().use { output ->
            val prefix = buildString {
                append("HTTP/1.1 200 OK\r\nContent-Length: ${response.size}\r\nConnection: close\r\n")
                extraHeaders.forEach { (name, value) -> append("$name: $value\r\n") }
                append("\r\n")
            }
            output.write(prefix.toByteArray(Charsets.US_ASCII))
            val truncate = path == "/api/v1/sync" && truncateNextResponse
            if (truncate) truncateNextResponse = false
            output.write(response, 0, if (truncate) response.size / 2 else response.size)
            output.flush()
        }
    }

    fun assertHealthy() { error.get()?.let { throw AssertionError("Test peer failed", it) } }

    override fun close() {
        socket.close()
        worker.join(5000)
        assertHealthy()
    }
}
