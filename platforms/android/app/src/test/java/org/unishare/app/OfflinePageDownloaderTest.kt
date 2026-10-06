package org.unishare.app

import java.io.File
import java.net.InetAddress
import java.net.URI
import org.junit.Assert.assertFalse
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class OfflinePageDownloaderTest {
    @Test
    fun `rejects local and private addresses`() {
        assertTrue(OfflineNetworkPolicy.isUnsafe(InetAddress.getByName("127.0.0.1")))
        assertTrue(OfflineNetworkPolicy.isUnsafe(InetAddress.getByName("192.168.1.20")))
        assertTrue(OfflineNetworkPolicy.isUnsafe(InetAddress.getByName("fc00::1")))
        assertFalse(OfflineNetworkPolicy.isUnsafe(InetAddress.getByName("93.184.216.34")))
    }

    @Test
    fun `creates a portable bounded filename`() {
        assertTrue(
            OfflineDownloadNames.fromUri(URI("https://example.com/a%20b/report?.pdf"), "application/pdf")
                .all { it.isLetterOrDigit() || it in "._-" },
        )
        assertTrue(OfflineDownloadNames.fromUri(URI("https://example.com/"), "text/html").endsWith(".html"))
    }

    @Test
    fun `html snapshot removes active content and external resources`() {
        val file = File.createTempFile("unishare-html-", ".html")
        try {
            file.writeText(
                "<html><head><title>Ejemplo</title><script>alert(1)</script></head>" +
                    "<body><p>Texto &amp; datos</p><img src='https://tracker.example/a'></body></html>",
            )
            OfflineHtmlSnapshot.rewrite(file, URI("https://example.com/article"))
            val result = file.readText()
            assertFalse(result.contains("alert(1)"))
            assertFalse(result.contains("tracker.example"))
            assertTrue(result.contains("Texto &amp; datos"))
            assertTrue(result.contains("default-src 'none'"))
        } finally {
            file.delete()
        }
    }

    @Test
    fun `extracts useful metadata from html`() {
        val metadata = OfflineHtmlSnapshot.extractMetadata(
            """<html><head><title>Artículo real</title><meta property="og:site_name" content="Revista"><meta name="author" content="Ada"><meta name="description" content="Texto promocional"></head><body><article><p>El artículo analiza cómo conservar una biblioteca personal sin depender de servicios externos. También explica por qué las copias locales facilitan la recuperación de los datos.</p></article></body></html>""",
            URI("https://example.com/article"),
        )

        assertEquals("Artículo real", metadata.title)
        assertEquals("Revista", metadata.source)
        assertEquals("Ada", metadata.author)
        assertEquals(
            "El artículo analiza cómo conservar una biblioteca personal sin depender de servicios externos. " +
                "También explica por qué las copias locales facilitan la recuperación de los datos.",
            metadata.description,
        )
        assertEquals("Texto promocional", metadata.promotionalDescription)
    }

    @Test
    fun `prefers open graph metadata and supports twitter fallbacks`() {
        val metadata = OfflineHtmlSnapshot.extractMetadata(
            """
            <html><head><title>Título del documento</title>
            <meta name="description" content="Descripción genérica">
            <meta name="twitter:title" content="Título Twitter">
            <meta name="twitter:description" content="Descripción Twitter">
            <meta property="og:title" content="Título de origen">
            <meta property="og:description" content="Descripción de origen">
            <meta name="application-name" content="Aplicación">
            <meta property="article:author" content="Autora original"></head><body><main>
            <p>El contenido real estudia la conservación privada de enlaces y documentos en varios dispositivos. Expone las decisiones necesarias para mantenerlos accesibles sin una nube obligatoria.</p>
            </main></body></html>
            """.trimIndent(),
            URI("https://example.com/article"),
        )

        assertEquals("Título de origen", metadata.title)
        assertEquals("Aplicación", metadata.source)
        assertEquals("Autora original", metadata.author)
        assertEquals(
            "El contenido real estudia la conservación privada de enlaces y documentos en varios dispositivos. " +
                "Expone las decisiones necesarias para mantenerlos accesibles sin una nube obligatoria.",
            metadata.description,
        )
        assertEquals("Descripción de origen", metadata.promotionalDescription)
        assertEquals(metadata.description, metadata.resolveDescription("Descripción de origen"))
        assertEquals("Mis notas", metadata.resolveDescription("Mis notas"))
    }

    @Test
    fun `prefers structured article body and ignores navigation`() {
        val metadata = OfflineHtmlSnapshot.extractMetadata(
            """
            <html><head><title>Informe</title><meta name="description" content="Compra ahora"></head><body>
            <nav><p>Portada Artículos Suscríbete Política de privacidad y otras secciones del sitio.</p></nav>
            <script type="application/ld+json">
            {"@context":"https://schema.org","@type":"Article","articleBody":"La investigación compara tres métodos de almacenamiento local y mide su resistencia ante fallos. Los resultados muestran que las copias verificadas por hash reducen los errores silenciosos."}
            </script>
            <main><p>Este texto alternativo no debe ganar cuando el autor publica articleBody de forma explícita y válida.</p></main>
            </body></html>
            """.trimIndent(),
            URI("https://example.com/report"),
        )

        assertEquals(
            "La investigación compara tres métodos de almacenamiento local y mide su resistencia ante fallos. " +
                "Los resultados muestran que las copias verificadas por hash reducen los errores silenciosos.",
            metadata.description,
        )
        assertEquals("Compra ahora", metadata.promotionalDescription)
    }

    @Test
    fun `never uses promotional copy as the content summary`() {
        val metadata = OfflineHtmlSnapshot.extractMetadata(
            """
            <html><head><title>Producto</title>
            <meta property="og:description" content="La mejor oferta del año: compra hoy"></head>
            <body><main><p>Inicio</p><p>Comprar</p></main></body></html>
            """.trimIndent(),
            URI("https://example.com/product"),
        )

        assertNull(metadata.description)
        assertEquals("La mejor oferta del año: compra hoy", metadata.promotionalDescription)
        assertNull(metadata.resolveDescription("La mejor oferta del año: compra hoy"))
    }

    @Test
    fun `selects the most substantive semantic region`() {
        val metadata = OfflineHtmlSnapshot.extractMetadata(
            """
            <html><head><title>Investigación</title></head><body>
            <article><p>Una tarjeta secundaria anuncia otra noticia y contiene suficiente texto para parecer un resumen válido.</p></article>
            <main>
            <p>La investigación principal documenta cómo se recuperaron los datos después de simular fallos en varios dispositivos. Detalla los resultados obtenidos y las comprobaciones realizadas sobre cada copia.</p>
            <p>El análisis concluye que la verificación periódica detecta daños antes de que afecten a la biblioteca personal.</p>
            </main></body></html>
            """.trimIndent(),
            URI("https://example.com/research"),
        )

        val summary = requireNotNull(metadata.description)
        assertTrue(summary.startsWith("La investigación principal documenta"))
        assertFalse(summary.contains("tarjeta secundaria", ignoreCase = true))
    }

    @Test
    fun `uses YouTube video description and replaces previously captured navigation`() {
        val metadata = OfflineHtmlSnapshot.extractMetadata(
            """
            <html><head><title>Vídeo útil - YouTube</title><meta name="description" content="Descripción truncada"></head><body>
            <div>OverPersAuteursrechtContactCreatorsAdverterenOntwikkelaarsVoorwaardenPrivacyBeleid en veiligheidZo werkt YouTubeNieuwe functies testen</div>
            <script>var ytInitialPlayerResponse={"videoDetails":{"shortDescription":"Este vídeo explica cómo organizar una biblioteca personal y conservar cada elemento sin depender de una nube. También muestra el flujo completo de principio a fin."}};</script>
            </body></html>
            """.trimIndent(),
            URI("https://www.youtube.com/watch?v=M7lc1UVf-VE"),
        )

        val summary = requireNotNull(metadata.description)
        assertTrue(summary.startsWith("Este vídeo explica"))
        assertFalse(summary.contains("Auteursrecht", ignoreCase = true))
        assertEquals(
            summary,
            metadata.resolveDescription(
                "OverPersAuteursrechtContactCreatorsAdverterenOntwikkelaarsVoorwaardenPrivacyBeleid en " +
                    "veiligheidZo werkt YouTubeNieuwe functies testen",
            ),
        )
        assertEquals("Mi nota personal", metadata.resolveDescription("Mi nota personal"))
        assertEquals(
            "Developers must review copyright terms, privacy policy and contact us before publishing.",
            metadata.resolveDescription(
                "Developers must review copyright terms, privacy policy and contact us before publishing.",
            ),
        )
    }

    @Test
    fun `uses typed structured description instead of whole body navigation`() {
        val metadata = OfflineHtmlSnapshot.extractMetadata(
            """
            <html><head><title>Grabación</title></head><body>
            <script type="application/ld+json">{"@type":"VideoObject","description":"La grabación documenta el proceso real de restauración y verifica el resultado con una copia local. Incluye los pasos necesarios para repetir la comprobación."}</script>
            <div>AboutPressCopyrightContact usCreatorsAdvertiseDevelopersTermsPrivacyPolicy</div>
            </body></html>
            """.trimIndent(),
            URI("https://example.com/video"),
        )

        val summary = requireNotNull(metadata.description)
        assertTrue(summary.startsWith("La grabación documenta"))
        assertFalse(summary.contains("Copyright", ignoreCase = true))
    }

    @Test
    fun `leaves summary empty when body contains only unmarked navigation`() {
        val metadata = OfflineHtmlSnapshot.extractMetadata(
            """
            <html><head><title>Página dinámica</title></head><body>
            <div>AboutPressCopyrightContact usCreatorsAdvertiseDevelopersTermsPrivacyPolicyHow YouTube worksTest new features</div>
            </body></html>
            """.trimIndent(),
            URI("https://example.com/dynamic"),
        )

        assertNull(metadata.description)
    }
}
