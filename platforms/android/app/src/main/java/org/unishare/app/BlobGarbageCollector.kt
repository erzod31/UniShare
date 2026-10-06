package org.unishare.app

import java.io.File
import java.nio.file.Files

object BlobGarbageCollector {
    private val canonicalHash = Regex("^[0-9a-f]{64}$")

    fun collect(blobRoot: File, referencedHashes: Set<String>, cutoffMillis: Long): Int {
        if (!blobRoot.isDirectory) return 0
        var deleted = 0
        blobRoot.walkTopDown().filter(File::isFile).forEach { file ->
            val hash = file.name
            if (!canonicalHash.matches(hash) || hash in referencedHashes ||
                file.lastModified() > cutoffMillis || Files.isSymbolicLink(file.toPath())) {
                return@forEach
            }
            val expected = File(blobRoot, "${hash.substring(0, 2)}/${hash.substring(2, 4)}/$hash")
            if (file.canonicalFile != expected.canonicalFile) return@forEach
            if (file.delete()) deleted++
        }
        blobRoot.walkBottomUp().filter { it != blobRoot && it.isDirectory }.forEach { directory ->
            if (directory.list()?.isEmpty() == true) directory.delete()
        }
        return deleted
    }
}
