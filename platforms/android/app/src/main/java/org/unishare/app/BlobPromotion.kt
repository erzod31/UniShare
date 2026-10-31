package org.unishare.app

import android.content.Context
import java.io.File
import java.io.FileOutputStream
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.util.UUID

/** Promotes only verified content and preserves a damaged predecessor before atomic replacement. */
object BlobPromotion {
    fun promote(context: Context, staged: File, destination: File, hash: String, length: Long): Boolean {
        require(LocalBlobIntegrity.matches(staged, hash, length)) { "El archivo preparado no supera la verificación de integridad" }
        require(!Files.isSymbolicLink(destination.toPath())) { "No se reemplazan enlaces simbólicos del almacén" }
        if (LocalBlobIntegrity.matches(destination, hash, length)) return false

        destination.parentFile?.mkdirs()
        if (destination.exists()) {
            require(destination.isFile) { "La ruta del blob no es un archivo" }
            val recovery = File(context.filesDir, "recovery/corrupt-blobs/$hash-${UUID.randomUUID()}.corrupt")
            recovery.parentFile?.mkdirs()
            try {
                FileOutputStream(recovery).use { output ->
                    destination.inputStream().use { it.copyTo(output, 128 * 1024) }
                    output.fd.sync()
                }
            } catch (error: Exception) {
                recovery.delete()
                throw error
            }
        }
        Files.move(staged.toPath(), destination.toPath(),
            StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING)
        return true
    }
}
