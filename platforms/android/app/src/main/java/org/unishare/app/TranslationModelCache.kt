package org.unishare.app

import org.json.JSONObject
import java.io.File
import java.io.FilterInputStream
import java.io.InputStream
import java.net.HttpURLConnection
import java.net.URI
import java.net.URL
import java.security.MessageDigest
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap

/** Immutable public model files only. No description or library metadata is accepted here. */
internal class TranslationModelCache(
    catalogJson: String,
    private val directory: File,
    private val openRemote: (URL) -> InputStream = ::openModelConnection,
) : AutoCloseable {
    private data class Entry(val hash: String, val size: Long, val url: String)
    private val entries = mutableMapOf<String, Entry>()
    private val streams = ConcurrentHashMap.newKeySet<InputStream>()
    @Volatile private var closed = false

    init {
        val pairs = JSONObject(catalogJson).getJSONObject("pairs")
        pairs.keys().forEach { pair ->
            val files = pairs.getJSONObject(pair).getJSONObject("files")
            files.keys().forEach { type ->
                val file = files.getJSONObject(type)
                val hash = file.getString("sha256")
                val size = file.getLong("size")
                val uri = URI(file.getString("url"))
                require(HASH.matches(hash) && HASH.matches(file.getString("decompressedSha256")))
                require(size in 1..67_108_864 && file.getLong("decompressedSize") in 1..134_217_728)
                require(uri.scheme == "https" && uri.host == "firefox-settings-attachments.cdn.mozilla.net" &&
                    uri.rawUserInfo == null && uri.rawQuery == null && uri.rawFragment == null && uri.port == -1)
                val entry = Entry(hash, size, uri.toString())
                require(entries[hash]?.size?.let { it == size } != false)
                entries[hash] = entry
            }
        }
    }

    @Synchronized
    fun get(hash: String): File {
        require(HASH.matches(hash))
        val entry = requireNotNull(entries[hash]) { "Unknown translation model" }
        check(!closed) { "Translation engine stopped" }
        val destination = File(directory, hash)
        if (destination.isFile && destination.length() == entry.size && digest(destination) == hash) return destination
        check(directory.isDirectory || directory.mkdirs())
        val partial = File(directory, "$hash.${UUID.randomUUID()}.partial")
        try {
            val digest = MessageDigest.getInstance("SHA-256")
            val input = openRemote(URI(entry.url).toURL())
            streams.add(input)
            try {
                input.use { source ->
                    partial.outputStream().use { output ->
                        val buffer = ByteArray(65_536)
                        var total = 0L
                        while (true) {
                            check(!closed) { "Translation engine stopped" }
                            val count = source.read(buffer)
                            if (count < 0) break
                            total += count
                            require(total <= entry.size) { "Translation model length mismatch" }
                            digest.update(buffer, 0, count)
                            output.write(buffer, 0, count)
                        }
                        require(total == entry.size && digest.digest().hex() == hash) { "Translation model integrity mismatch" }
                        output.fd.sync()
                    }
                }
            } finally { streams.remove(input) }
            java.nio.file.Files.move(partial.toPath(), destination.toPath(),
                java.nio.file.StandardCopyOption.REPLACE_EXISTING, java.nio.file.StandardCopyOption.ATOMIC_MOVE)
            return destination
        } finally { partial.delete() }
    }

    override fun close() {
        closed = true
        streams.forEach { runCatching { it.close() } }
    }

    companion object {
        private val HASH = Regex("[a-f0-9]{64}")
        private fun digest(file: File): String = file.inputStream().use { input ->
            val digest = MessageDigest.getInstance("SHA-256")
            val buffer = ByteArray(65_536)
            while (true) { val count = input.read(buffer); if (count < 0) break; digest.update(buffer, 0, count) }
            digest.digest().hex()
        }
        internal fun ByteArray.hex(): String = joinToString("") { "%02x".format(it.toInt() and 0xff) }
    }
}

private fun openModelConnection(url: URL): InputStream {
    val connection = url.openConnection() as HttpURLConnection
    connection.connectTimeout = 30_000
    connection.readTimeout = 180_000
    connection.instanceFollowRedirects = false
    try {
        require(connection.responseCode == 200) { "Translation model unavailable" }
        return object : FilterInputStream(connection.inputStream) {
            override fun close() { try { super.close() } finally { connection.disconnect() } }
        }
    } catch (exception: Exception) { connection.disconnect(); throw exception }
}
