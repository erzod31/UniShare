package org.unishare.app

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream
import java.net.HttpURLConnection
import java.net.InetAddress
import java.net.ServerSocket
import java.net.URI
import java.net.URL
import java.util.concurrent.atomic.AtomicReference
import java.util.zip.GZIPOutputStream
import kotlin.concurrent.thread
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class OfflineDownloadRuntimeTest {
    private val base = InstrumentationRegistry.getInstrumentation().targetContext
    private val uri = URI("https://example.invalid/download.txt")

    @Test
    fun shortResponseIsRejectedAndThePartialIsRemoved() {
        IsolatedLibraryContext(base).use { context ->
            val connection = TestResponse("incompleto".toByteArray(), 200)
            val result = runCatching { OfflinePageDownloader(context).readResponse(connection, uri) }
            result.getOrNull()?.file?.delete()
            assertTrue("A short stream must not become downloaded content", result.isFailure)
            assertTrue(java.io.File(context.filesDir, "staging").listFiles().orEmpty().isEmpty())
        }
    }

    @Test
    fun completeResponsesWithAndWithoutAnnouncedLengthRetainExactBytes() {
        IsolatedLibraryContext(base).use { context ->
            val bytes = "Contenido íntegro: café, información y enlaces.".toByteArray()
            for (length in listOf(bytes.size.toLong(), -1L)) {
                val content = OfflinePageDownloader(context).readResponse(TestResponse(bytes, length), uri)
                assertArrayEquals(bytes, content.file.readBytes())
                assertEquals(bytes.size.toLong(), content.byteLength)
                assertEquals("text/plain", content.mimeType)
                content.file.delete()
            }
        }
    }

    @Test
    fun transparentGzipIsNotComparedAgainstTheCompressedWireLength() {
        IsolatedLibraryContext(base).use { context ->
            val bytes = "Contenido comprimido con acentos.\n".repeat(100).toByteArray()
            val compressed = ByteArrayOutputStream().also { out ->
                GZIPOutputStream(out).use { it.write(bytes) }
            }.toByteArray()
            val failure = AtomicReference<Throwable?>()
            ServerSocket(0, 1, InetAddress.getByName("127.0.0.1")).use { server ->
                server.soTimeout = 10_000
                val worker = thread(isDaemon = true) {
                    try {
                        server.accept().use { client ->
                            client.soTimeout = 10_000
                            val reader = client.getInputStream().bufferedReader(Charsets.US_ASCII)
                            while (!reader.readLine().isNullOrEmpty()) { /* consume request headers */ }
                            client.getOutputStream().apply {
                                write(("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\n" +
                                    "Content-Encoding: gzip\r\nContent-Length: ${compressed.size}\r\n" +
                                    "Connection: close\r\n\r\n").toByteArray(Charsets.US_ASCII))
                                write(compressed)
                                flush()
                            }
                        }
                    } catch (error: Throwable) { failure.set(error) }
                }
                val connection = URL("http://127.0.0.1:${server.localPort}/gzip").openConnection() as HttpURLConnection
                connection.connectTimeout = 10_000
                connection.readTimeout = 10_000
                try {
                    assertEquals(200, connection.responseCode)
                    val content = OfflinePageDownloader(context).readResponse(connection, uri)
                    assertArrayEquals(bytes, content.file.readBytes())
                    assertEquals(bytes.size.toLong(), content.byteLength)
                    content.file.delete()
                } finally {
                    connection.disconnect()
                    worker.join(12_000)
                }
                assertFalse("HTTP fixture did not finish", worker.isAlive)
                failure.get()?.let { throw AssertionError("HTTP fixture failed", it) }
            }
        }
    }

    private class TestResponse(private val bytes: ByteArray, private val length: Long) :
        HttpURLConnection(URL("https://example.invalid/test")) {
        override fun connect() = Unit
        override fun disconnect() = Unit
        override fun usingProxy() = false
        override fun getInputStream() = ByteArrayInputStream(bytes)
        override fun getContentLengthLong() = length
        override fun getContentType() = "text/plain"
    }
}
