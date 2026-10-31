package org.unishare.app

import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test
import java.io.ByteArrayInputStream
import java.io.File
import java.nio.file.Files
import java.security.MessageDigest
import java.util.concurrent.CancellationException

class AppUpdatesTest {
    private val repository = "erzod31/UniShare"
    private val bytes = "Synthetic APK; never executable".toByteArray()
    private fun candidate() = AppUpdateCandidate(repository, "0.6.25", "android", "UniShare-Android-0.6.25.apk",
        "https://github.com/$repository/releases/download/v0.6.25/UniShare-Android-0.6.25.apk", bytes.size.toLong(),
        MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { "%02x".format(it.toInt() and 255) })
    private fun release(version: String = "0.6.25", mutate: (JSONObject) -> Unit = {}): String {
        val name = "UniShare-Android-$version.apk"
        val asset = JSONObject().put("name", name).put("state", "uploaded").put("size", bytes.size)
            .put("browser_download_url", "https://github.com/$repository/releases/download/v$version/$name")
            .put("digest", "sha256:${candidate().sha256}")
        return JSONObject().put("draft", false).put("prerelease", false).put("tag_name", "v$version")
            .put("html_url", "https://github.com/$repository/releases/tag/v$version").put("assets", JSONArray().put(asset)).also(mutate).toString()
    }

    @Test fun onlyNewerStableReleasesAreOffered() {
        for (version in listOf("0.6.16", "0.6.24")) assertNull(AppUpdatePolicy.parseRelease(release(version), repository, "0.6.24").candidate)
        assertNotNull(AppUpdatePolicy.parseRelease(release(), repository, "0.6.24").candidate)
        assertNull(AppUpdatePolicy.parseRelease(release { it.put("prerelease", true) }, repository, "0.6.24").candidate)
        for (version in listOf("1.2", "01.2.3", "1.2.3-beta", "1.2.3.4", "99999999999999.1.1")) rejects { AppUpdateVersion.parse(version) }
    }
    @Test fun malformedOrUntrustedManifestsAreRejected() {
        for (json in listOf("{}", "[]", release { it.put("html_url", "https://github.com/other/product/releases/tag/v0.6.25") },
            release { it.getJSONArray("assets").getJSONObject(0).put("digest", JSONObject.NULL) },
            release { it.getJSONArray("assets").getJSONObject(0).put("browser_download_url", "http://127.0.0.1/other.apk") },
            release { it.getJSONArray("assets").put(it.getJSONArray("assets").getJSONObject(0)) })) {
            rejects { AppUpdatePolicy.parseRelease(json, repository, "0.6.24") }
        }
        rejects { AppUpdatePolicy.parseRelease("[".repeat(40) + "]".repeat(40), repository, "0.6.24") }
    }
    @Test fun packageIdentityVersionCodeAndExactSigningIdentityAreRequired() {
        val installed = AndroidUpdateIdentity("org.unishare.app", "0.6.24", 37, setOf("original-certificate"))
        val valid = installed.copy(versionName = "0.6.25", versionCode = 38)
        AndroidUpdateIdentityPolicy.verify(installed, valid, "0.6.25")
        for (invalid in listOf(valid.copy(packageName = "other.app"), valid.copy(versionName = "0.6.16"),
            valid.copy(versionCode = 37), valid.copy(certificateHashes = setOf("other")), valid.copy(certificateHashes = emptySet()))) {
            rejects { AndroidUpdateIdentityPolicy.verify(installed, invalid, "0.6.25") }
        }
    }
    @Test fun verifiedFlowAndUserCancelUseOnlyFakeInstaller() = fixture { root ->
        var applied = 0
        var verified = 0
        val client = client(root, verifier = AppUpdatePackageVerifier { _, _ -> verified++; "synthetic" },
            installer = AppUpdateInstaller { applied++; AppUpdateInstallResult.REQUESTED })
        val ready = client.download(candidate(), AppUpdateCancellation())
        assertArrayEquals(bytes, ready.file.readBytes())
        assertEquals(AppUpdateInstallResult.CANCELED, client.requestInstall(ready, false, AppUpdateCancellation()))
        assertEquals(0, applied)
        assertEquals(AppUpdateInstallResult.REQUESTED, client.requestInstall(ready, true, AppUpdateCancellation()))
        assertEquals(1, applied)
        assertEquals(2, verified)
    }
    @Test fun hashSizeTruncationSignatureAndNetworkFailuresNeverProduceReadyPackage() {
        for (failure in listOf("hash", "size", "truncated", "oversized", "signature", "network")) fixture { root ->
            var applied = 0
            val content = when (failure) { "hash" -> ByteArray(bytes.size); "size", "truncated" -> bytes.dropLast(1).toByteArray(); "oversized" -> bytes + 1.toByte(); else -> bytes }
            val client = client(root,
                verifier = AppUpdatePackageVerifier { _, _ -> require(failure != "signature"); "synthetic" },
                installer = AppUpdateInstaller { applied++; AppUpdateInstallResult.REQUESTED },
                transport = AppUpdateTransport { _, _, _ ->
                    check(failure != "network")
                    AppUpdateResponse(200, if (failure == "size") content.size.toLong() else bytes.size.toLong(), ByteArrayInputStream(content))
                })
            rejects { client.download(candidate(), AppUpdateCancellation()) }
            assertEquals(0, applied)
            assertTrue(root.walkTopDown().none { it.extension == "apk" || it.extension == "part" })
        }
    }
    @Test fun cancelDuringTransferRemovesPartialAndTamperingPreventsNativeLaunch() = fixture { root ->
        val client = client(root)
        val token = AppUpdateCancellation()
        try { client.download(candidate(), token) { token.cancel() }; fail("Expected cancellation") } catch (_: CancellationException) {}
        assertTrue(root.walkTopDown().none { it.extension == "part" })
        val ready = client.download(candidate(), AppUpdateCancellation())
        ready.file.writeBytes(ByteArray(bytes.size))
        rejects { client.requestInstall(ready, true, AppUpdateCancellation()) }
    }
    @Test fun requestPermissionResultPreservesVerifiedPackageAndWrongPlatformNeverDownloads() = fixture { root ->
        val client = client(root, installer = AppUpdateInstaller { AppUpdateInstallResult.PERMISSION_REQUIRED })
        val ready = client.download(candidate(), AppUpdateCancellation())
        assertEquals(AppUpdateInstallResult.PERMISSION_REQUIRED, client.requestInstall(ready, true, AppUpdateCancellation()))
        assertTrue(ready.file.isFile)
        rejects { client.download(candidate().copy(platform = "windows-x64"), AppUpdateCancellation()) }
        rejects { client.download(candidate().copy(version = "0.6.16"), AppUpdateCancellation()) }
    }
    @Test fun actualOlderChannelAnd404DoNotOfferDowngrade() = fixture { root ->
        for (status in listOf(200, 404)) {
            val client = client(root, transport = AppUpdateTransport { _, _, _ -> AppUpdateResponse(status, -1, ByteArrayInputStream(release("0.6.16").toByteArray())) })
            assertNull(client.check(AppUpdateCancellation()).candidate)
        }
    }
    private fun client(root: File, verifier: AppUpdatePackageVerifier = AppUpdatePackageVerifier { _, _ -> "synthetic" },
        installer: AppUpdateInstaller = AppUpdateInstaller { AppUpdateInstallResult.REQUESTED },
        transport: AppUpdateTransport = AppUpdateTransport { _, _, _ -> AppUpdateResponse(200, bytes.size.toLong(), ByteArrayInputStream(bytes)) }) =
        AppUpdateClient(repository, "0.6.24", root, verifier, installer, transport)
    private fun rejects(action: () -> Unit) { try { action(); fail("Expected rejection") } catch (_: Exception) {} }
    private fun fixture(action: (File) -> Unit) {
        val root = Files.createTempDirectory("unishare-app-update-test-").toFile()
        try { action(root) } finally { check(root.canonicalPath.startsWith(File(checkNotNull(System.getProperty("java.io.tmpdir"))).canonicalPath + File.separator)); root.deleteRecursively() }
    }
}
