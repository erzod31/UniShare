package org.unishare.app

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test
import java.nio.charset.StandardCharsets

class FileContentPolicyTest {
    @Test
    fun `signature replaces a generic provider type`() {
        val content = byteArrayOf(
            0x89.toByte(), 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a,
        )

        val result = FileContentPolicy.inspect(content, "application/octet-stream", "image.bin")

        assertEquals("image/png", result.mimeType)
    }

    @Test
    fun `recognized contradictory signatures are rejected`() {
        val content = "%PDF-1.7\n".toByteArray(StandardCharsets.US_ASCII)

        val error = assertThrows(IllegalArgumentException::class.java) {
            FileContentPolicy.inspect(content, "image/png", "document.png")
        }

        assertEquals(
            "El contenido del archivo no coincide con el tipo declarado por la aplicación de origen.",
            error.message,
        )
    }

    @Test
    fun `declared signed type requires its signature`() {
        val error = assertThrows(IllegalArgumentException::class.java) {
            FileContentPolicy.inspect("not a png".toByteArray(), "image/png", "image.png")
        }

        assertEquals("El archivo no contiene la firma esperada para el tipo declarado.", error.message)
    }

    @Test
    fun `html disguised as text is rejected`() {
        val error = assertThrows(IllegalArgumentException::class.java) {
            FileContentPolicy.inspect(
                "  <!DOCTYPE html><script>alert(1)</script>".toByteArray(),
                "text/plain",
                "notes.txt",
            )
        }

        assertEquals(
            "No se importa HTML o SVG activo como archivo. Guarda el enlace o crea una copia offline segura.",
            error.message,
        )
    }

    @Test
    fun `active extension is rejected even when provider says plain text`() {
        assertThrows(IllegalArgumentException::class.java) {
            FileContentPolicy.inspect("texto".toByteArray(), "text/plain", "page.HTML")
        }
    }

    @Test
    fun `svg with xml preamble is rejected`() {
        assertThrows(IllegalArgumentException::class.java) {
            FileContentPolicy.inspect(
                "<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"></svg>".toByteArray(),
                "application/octet-stream",
                "drawing.bin",
            )
        }
    }

    @Test
    fun `office zip container keeps its useful provider type`() {
        val content = byteArrayOf(0x50, 0x4b, 0x03, 0x04)

        val result = FileContentPolicy.inspect(
            content,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "report.docx",
        )

        assertEquals(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            result.mimeType,
        )
    }

    @Test
    fun `office extension recovers type when provider is generic`() {
        val content = byteArrayOf(0x50, 0x4b, 0x03, 0x04)

        val result = FileContentPolicy.inspect(content, "application/octet-stream", "report.docx")

        assertEquals(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            result.mimeType,
        )
    }

    @Test
    fun `heic brand is not mistaken for mp4 video`() {
        val content = byteArrayOf(
            0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70,
            0x68, 0x65, 0x69, 0x63,
        )

        val result = FileContentPolicy.inspect(content, "image/heic", "photo.heic")

        assertEquals("image/heif", result.mimeType)
    }

    @Test
    fun `m4a brand is stored as audio`() {
        val content = byteArrayOf(
            0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70,
            0x4d, 0x34, 0x41, 0x20,
        )

        val result = FileContentPolicy.inspect(content, "application/octet-stream", "recording.m4a")

        assertEquals("audio/mp4", result.mimeType)
    }

    @Test
    fun `unknown plain document keeps bounded declared type`() {
        val result = FileContentPolicy.inspect(
            "Contenido normal".toByteArray(),
            "text/plain; charset=utf-8",
            "notes.txt",
        )

        assertEquals("text/plain", result.mimeType)
    }
}
