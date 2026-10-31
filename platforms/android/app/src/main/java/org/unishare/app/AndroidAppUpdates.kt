package org.unishare.app

import android.app.Activity
import android.content.Intent
import android.content.pm.PackageInfo
import android.content.pm.PackageManager
import android.os.Build
import android.provider.Settings
import androidx.core.content.FileProvider
import androidx.core.net.toUri
import java.io.File
import java.security.MessageDigest

internal data class AndroidUpdateIdentity(val packageName: String, val versionName: String,
    val versionCode: Long, val certificateHashes: Set<String>)

internal object AndroidUpdateIdentityPolicy {
    fun verify(installed: AndroidUpdateIdentity, candidate: AndroidUpdateIdentity, version: String) {
        require(installed.packageName == candidate.packageName && candidate.versionName == version &&
            AppUpdateVersion.parse(candidate.versionName) > AppUpdateVersion.parse(installed.versionName) &&
            candidate.versionCode > installed.versionCode && installed.certificateHashes.isNotEmpty() &&
            candidate.certificateHashes == installed.certificateHashes) { "APK package, version or signing certificate mismatch" }
    }
}

internal class AndroidAppUpdateVerifier(private val activity: Activity, private val packageName: String) : AppUpdatePackageVerifier {
    override fun verify(file: File, candidate: AppUpdateCandidate): String {
        require(activity.packageName == packageName && candidate.platform == "android") { "Production updates are disabled for QA packages" }
        val manager = activity.packageManager
        val flags = if (Build.VERSION.SDK_INT >= 28) PackageManager.GET_SIGNING_CERTIFICATES else @Suppress("DEPRECATION") PackageManager.GET_SIGNATURES
        val installed = manager.getPackageInfo(activity.packageName, flags)
        val archive = manager.getPackageArchiveInfo(file.absolutePath, flags) ?: error("APK cannot be inspected")
        AndroidUpdateIdentityPolicy.verify(identity(installed), identity(archive), candidate.version)
        return "matching-certificate"
    }

    @Suppress("DEPRECATION") // Required for API 26/27; modern devices use SigningInfo and longVersionCode.
    private fun identity(info: PackageInfo): AndroidUpdateIdentity {
        val signatures = if (Build.VERSION.SDK_INT >= 28) info.signingInfo?.apkContentsSigners else info.signatures
        val hashes = signatures.orEmpty().map {
            MessageDigest.getInstance("SHA-256").digest(it.toByteArray()).joinToString("") { byte -> "%02x".format(byte.toInt() and 255) }
        }.toSet()
        return AndroidUpdateIdentity(info.packageName, info.versionName ?: error("Missing APK version"),
            if (Build.VERSION.SDK_INT >= 28) info.longVersionCode else info.versionCode.toLong(), hashes)
    }
}

internal class AndroidAppUpdateInstaller(private val activity: Activity) : AppUpdateInstaller {
    override fun request(update: VerifiedAppUpdate): AppUpdateInstallResult {
        if (!activity.packageManager.canRequestPackageInstalls()) {
            activity.startActivity(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, "package:${activity.packageName}".toUri()))
            return AppUpdateInstallResult.PERMISSION_REQUIRED
        }
        val uri = FileProvider.getUriForFile(activity, "${activity.packageName}.files", update.file)
        activity.startActivity(Intent(Intent.ACTION_VIEW).apply {
            setDataAndType(uri, "application/vnd.android.package-archive")
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        })
        return AppUpdateInstallResult.REQUESTED
    }
}
