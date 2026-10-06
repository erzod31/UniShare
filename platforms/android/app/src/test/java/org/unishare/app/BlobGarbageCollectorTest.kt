package org.unishare.app

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.nio.file.Files

class BlobGarbageCollectorTest {
    @Test fun deletesOnlyOldCanonicalUnreferencedBlobs() {
        val root = Files.createTempDirectory("unishare-gc-").toFile()
        try {
            val oldHash = "a".repeat(64)
            val old = blob(root, oldHash).apply { parentFile?.mkdirs(); writeText("orphan"); setLastModified(1_000) }
            val referencedHash = "b".repeat(64)
            val referenced = blob(root, referencedHash).apply {
                parentFile?.mkdirs(); writeText("referenced"); setLastModified(1_000)
            }
            val youngHash = "c".repeat(64)
            val young = blob(root, youngHash).apply { parentFile?.mkdirs(); writeText("young"); setLastModified(9_000) }
            val unknown = File(root, "manual-note.txt").apply { writeText("keep"); setLastModified(1_000) }

            val deleted = BlobGarbageCollector.collect(root, setOf(referencedHash), cutoffMillis = 5_000)

            assertEquals(1, deleted)
            assertFalse(old.exists())
            assertTrue(referenced.exists())
            assertTrue(young.exists())
            assertTrue(unknown.exists())
        } finally {
            root.deleteRecursively()
        }
    }

    private fun blob(root: File, hash: String) =
        File(root, "${hash.substring(0, 2)}/${hash.substring(2, 4)}/$hash")
}
