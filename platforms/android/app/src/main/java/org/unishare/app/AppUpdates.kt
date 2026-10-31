package org.unishare.app

import org.json.JSONObject
import java.io.Closeable
import java.io.File
import java.io.InputStream
import java.net.HttpURLConnection
import java.net.URI
import java.net.URL
import java.security.MessageDigest
import java.util.UUID
import java.util.concurrent.CancellationException

internal data class AppUpdateVersion(val major: Int, val minor: Int, val patch: Int) : Comparable<AppUpdateVersion> {
    override fun compareTo(other: AppUpdateVersion): Int = compareValuesBy(this, other, { it.major }, { it.minor }, { it.patch })
    override fun toString() = "$major.$minor.$patch"
    companion object {
        fun parse(value: String): AppUpdateVersion {
            require(value.length <= 32 && value.matches(Regex("(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)"))) { "Invalid release version" }
            val parts = value.split('.').map { it.toInt() }
            return AppUpdateVersion(parts[0], parts[1], parts[2])
        }
    }
}

internal data class AppUpdateCandidate(val repository: String, val version: String, val platform: String,
    val fileName: String, val url: String, val bytes: Long, val sha256: String)
internal data class AppUpdateCheck(val published: String?, val candidate: AppUpdateCandidate?)
internal data class VerifiedAppUpdate(val candidate: AppUpdateCandidate, val file: File, val signature: String)
internal enum class AppUpdateInstallResult { CANCELED, PERMISSION_REQUIRED, REQUESTED }
internal fun interface AppUpdatePackageVerifier { fun verify(file: File, candidate: AppUpdateCandidate): String }
internal fun interface AppUpdateInstaller { fun request(update: VerifiedAppUpdate): AppUpdateInstallResult }

internal object AppUpdatePolicy {
    const val MAX_PACKAGE_BYTES = 256L * 1024 * 1024
    const val MAX_METADATA_BYTES = 2 * 1024 * 1024
    fun validateRepository(repository: String) {
        require(repository.length <= 160 && repository.matches(Regex("[A-Za-z0-9_-]+/[A-Za-z0-9_.-]+")) &&
            repository.split('/').none { it == "." || it == ".." }) { "Invalid GitHub repository" }
    }
    fun fileName(platform: String, version: String) = when (platform) {
        "android" -> "UniShare-Android-$version.apk"
        "windows-x64" -> "UniShare-Windows-Setup-$version.exe"
        else -> error("Unsupported update platform")
    }
    fun validate(candidate: AppUpdateCandidate, repository: String, current: String) {
        validateRepository(repository)
        require(candidate.repository == repository && AppUpdateVersion.parse(candidate.version) > AppUpdateVersion.parse(current) &&
            candidate.fileName == fileName(candidate.platform, candidate.version) &&
            candidate.url == "https://github.com/$repository/releases/download/v${candidate.version}/${candidate.fileName}" &&
            candidate.bytes in 1..MAX_PACKAGE_BYTES && candidate.sha256.matches(Regex("[a-f0-9]{64}"))) {
            "Invalid update identity, version, origin, size or hash"
        }
    }
    fun parseRelease(json: String, repository: String, current: String, platform: String = "android"): AppUpdateCheck {
        validateRepository(repository)
        val installed = AppUpdateVersion.parse(current)
        require(json.toByteArray(Charsets.UTF_8).size <= MAX_METADATA_BYTES) { "Metadata too large" }
        var depth = 0
        var quoted = false
        var escaped = false
        json.forEach { char ->
            if (quoted) {
                if (escaped) escaped = false else if (char == '\\') escaped = true else if (char == '"') quoted = false
            } else when (char) {
                '"' -> quoted = true
                '{', '[' -> { depth++; require(depth <= 32) { "Metadata too deeply nested" } }
                '}', ']' -> depth--
            }
        }
        val release = JSONObject(json)
        if (release.getBoolean("draft") || release.getBoolean("prerelease")) return AppUpdateCheck(null, null)
        val tag = release.getString("tag_name")
        require(tag.startsWith('v')) { "Invalid release tag" }
        val version = AppUpdateVersion.parse(tag.substring(1))
        require(release.getString("html_url") == "https://github.com/$repository/releases/tag/$tag") { "Wrong release repository" }
        if (version <= installed) return AppUpdateCheck(version.toString(), null)
        val name = fileName(platform, version.toString())
        val assets = release.getJSONArray("assets")
        val matches = (0 until assets.length()).map { assets.getJSONObject(it) }.filter { it.getString("name") == name }
        require(matches.size == 1) { "A unique platform package is not published" }
        val asset = matches.single()
        require(asset.getString("state") == "uploaded") { "Incomplete upload" }
        val digest = asset.getString("digest")
        require(digest.matches(Regex("sha256:[a-f0-9]{64}"))) { "No authenticated SHA-256 digest" }
        val result = AppUpdateCandidate(repository, version.toString(), platform, name,
            asset.getString("browser_download_url"), asset.getLong("size"), digest.substring(7))
        validate(result, repository, current)
        return AppUpdateCheck(version.toString(), result)
    }
}

internal class AppUpdateCancellation {
    @Volatile private var canceled = false
    @Volatile var connection: HttpURLConnection? = null
    fun cancel() { canceled = true; connection?.disconnect() }
    fun check() { if (canceled || Thread.currentThread().isInterrupted) throw CancellationException("Update canceled") }
}

internal class AppUpdateResponse(val status: Int, val length: Long, val input: InputStream,
    private val release: () -> Unit = {}) : Closeable {
    override fun close() { try { input.close() } finally { release() } }
}
internal fun interface AppUpdateTransport {
    fun open(url: String, download: Boolean, cancellation: AppUpdateCancellation): AppUpdateResponse
}

internal class GitHubAppUpdateTransport : AppUpdateTransport {
    override fun open(url: String, download: Boolean, cancellation: AppUpdateCancellation): AppUpdateResponse {
        var location = url
        repeat(6) {
            cancellation.check()
            val uri = URI(location)
            require(uri.scheme == "https" && uri.userInfo == null && uri.fragment == null && (uri.port == -1 || uri.port == 443) &&
                if (download) uri.host in setOf("github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com")
                else uri.host == "api.github.com") { "Untrusted update origin or redirect" }
            val connection = URL(location).openConnection() as HttpURLConnection
            connection.instanceFollowRedirects = false
            connection.connectTimeout = 15_000
            connection.readTimeout = 15_000
            connection.setRequestProperty("User-Agent", "UniShare-AppUpdates/1")
            connection.setRequestProperty("Accept-Encoding", "identity")
            if (!download) connection.setRequestProperty("Accept", "application/vnd.github+json")
            cancellation.connection = connection
            try {
                val status = connection.responseCode
                if (status in 300..399) {
                    require(download) { "Metadata redirect rejected" }
                    val redirect = connection.getHeaderField("Location") ?: error("Missing redirect")
                    location = URI(location).resolve(redirect).toString()
                    connection.disconnect()
                } else {
                    val input = if (status == 200) connection.inputStream else connection.errorStream ?: java.io.ByteArrayInputStream(byteArrayOf())
                    return AppUpdateResponse(status, connection.contentLengthLong, input) { connection.disconnect(); cancellation.connection = null }
                }
            } catch (error: Exception) { connection.disconnect(); cancellation.check(); throw error }
        }
        error("Too many package redirects")
    }
}

internal class AppUpdateClient(private val repository: String, private val current: String, private val cache: File,
    private val verifier: AppUpdatePackageVerifier, private val installer: AppUpdateInstaller,
    private val transport: AppUpdateTransport = GitHubAppUpdateTransport()) {
    fun check(cancellation: AppUpdateCancellation): AppUpdateCheck {
        AppUpdatePolicy.validateRepository(repository)
        AppUpdateVersion.parse(current)
        transport.open("https://api.github.com/repos/$repository/releases/latest", false, cancellation).use { response ->
            if (response.status == 404) return AppUpdateCheck(null, null)
            check(response.status == 200) { "Update channel unavailable (${response.status})" }
            require(response.length <= AppUpdatePolicy.MAX_METADATA_BYTES) { "Metadata too large" }
            val output = java.io.ByteArrayOutputStream()
            copy(response.input, output, AppUpdatePolicy.MAX_METADATA_BYTES.toLong(), cancellation) {}
            return AppUpdatePolicy.parseRelease(output.toString("UTF-8"), repository, current)
        }
    }

    fun download(candidate: AppUpdateCandidate, cancellation: AppUpdateCancellation, progress: (Long) -> Unit = {}): VerifiedAppUpdate {
        validateAndroid(candidate)
        cancellation.check()
        val directory = File(cache, candidate.version + "-" + UUID.randomUUID()).apply { check(mkdirs()) }
        val target = File(directory, candidate.fileName)
        // Android's archive parser receives an .apk filename; its private staging directory is
        // never handed to the installer. Only a fully verified file is promoted out of it.
        val partial = File(directory, "partial/${candidate.fileName}")
        check(partial.parentFile!!.mkdirs())
        try {
            transport.open(candidate.url, true, cancellation).use { response ->
                check(response.status == 200) { "Package download failed (${response.status})" }
                require(response.length == -1L || response.length == candidate.bytes) { "Package length differs" }
                partial.outputStream().use { output ->
                    require(copy(response.input, output, candidate.bytes, cancellation, progress) == candidate.bytes) { "Truncated package" }
                }
            }
            verifyHash(partial, candidate, cancellation)
            val signature = verifier.verify(partial, candidate)
            cancellation.check()
            check(partial.renameTo(target)) { "Cannot promote verified package" }
            partial.parentFile?.delete()
            return VerifiedAppUpdate(candidate, target, signature)
        } catch (error: Exception) { partial.delete(); partial.parentFile?.delete(); throw error }
    }

    fun requestInstall(update: VerifiedAppUpdate, userConfirmed: Boolean, cancellation: AppUpdateCancellation): AppUpdateInstallResult {
        if (!userConfirmed) return AppUpdateInstallResult.CANCELED
        verifyReady(update, cancellation)
        return installer.request(update)
    }

    fun verifyReady(update: VerifiedAppUpdate, cancellation: AppUpdateCancellation) {
        validateAndroid(update.candidate)
        require(update.file.canonicalPath.startsWith(cache.canonicalPath + File.separator) && update.file.name == update.candidate.fileName) { "Package is outside the update cache" }
        verifyHash(update.file, update.candidate, cancellation)
        verifier.verify(update.file, update.candidate)
        cancellation.check()
    }

    private fun validateAndroid(candidate: AppUpdateCandidate) {
        AppUpdatePolicy.validate(candidate, repository, current)
        require(candidate.platform == "android") { "Not an Android update" }
    }
    private fun verifyHash(file: File, candidate: AppUpdateCandidate, cancellation: AppUpdateCancellation) {
        require(file.length() == candidate.bytes) { "Package size changed" }
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { stream ->
            val buffer = ByteArray(65536)
            while (true) {
                cancellation.check()
                val count = stream.read(buffer)
                if (count == -1) break
                digest.update(buffer, 0, count)
            }
        }
        val expected = candidate.sha256.chunked(2).map { it.toInt(16).toByte() }.toByteArray()
        require(MessageDigest.isEqual(digest.digest(), expected)) { "Package SHA-256 differs" }
    }
    private fun copy(input: InputStream, output: java.io.OutputStream, maximum: Long,
        cancellation: AppUpdateCancellation, progress: (Long) -> Unit): Long {
        val buffer = ByteArray(65536)
        var received = 0L
        while (true) {
            cancellation.check()
            val count = input.read(buffer)
            if (count == -1) break
            received += count
            require(received <= maximum) { "Response exceeds its limit" }
            output.write(buffer, 0, count)
            progress(received)
        }
        return received
    }
}
