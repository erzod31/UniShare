package org.unishare.app

import java.net.URI
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class TikTokMetadataTest {
    private val id = "7300000000000000001"
    private val uri = URI("https://www.tiktok.com/@creadora/video/$id")

    @Test
    fun verifiedLegacyOEmbedCaptionIsOriginalButOtherIdsAreRejected() {
        val payload = JSONObject().put("provider_name", "TikTok").put("type", "video")
            .put("embed_product_id", id).put("title", "🫶 #original").put("author_name", "Autora API")
        val metadata = checkNotNull(TikTokMetadata.readOEmbed(payload.toString(), uri))
        assertEquals("🫶 #original", metadata.description)
        assertTrue(metadata.originDescriptionFound)
        payload.put("embed_product_id", "7300000000000000002")
        assertNull(TikTokMetadata.readOEmbed(payload.toString(), uri))
    }

    @Test
    fun currentEmbedWithoutCaptionDoesNotInventText() {
        val payload = """{"provider_name":"TikTok","type":"video","embed_product_id":"$id","html":"<iframe></iframe>"}"""
        assertNull(TikTokMetadata.readOEmbed(payload, uri))
    }

    @Test
    fun fallbackDoesNotForwardTrackingOrCredentials() {
        val fallback = checkNotNull(TikTokMetadata.oEmbedUri(URI("$uri?utm_source=share&token=private-fixture#part")))
        assertEquals("https", fallback.scheme)
        assertEquals("www.tiktok.com", fallback.host)
        assertFalse(fallback.toString().contains("private-fixture"))
        assertFalse(fallback.toString().contains("utm_source"))
        assertNull(TikTokMetadata.oEmbedUri(URI("https://user:password@www.tiktok.com/@creadora/video/$id")))
    }

    @Test
    fun oversizedNestingIsRejectedBeforeNativeJsonParsing() {
        assertNull(OriginContentRules.parseJson("[".repeat(5000) + "0" + "]".repeat(5000)))
    }

    @Test
    fun unicodeCaptionAndTitleLimitsPreserveWholeCodePoints() {
        val caption = "🌊".repeat(4100)
        val state = JSONObject().put("ItemModule", JSONObject().put(id,
            JSONObject().put("id", id).put("desc", caption)))
        val metadata = checkNotNull(TikTokMetadata.extract("<script id='SIGI_STATE'>$state</script>", uri))
        assertEquals("🌊".repeat(3999) + "…", metadata.description)
        assertEquals("🌊".repeat(79) + "…", metadata.title)
        val json = JSONObject().put("@type", "VideoObject").put("name", "🌊".repeat(501)).put("description", "Pie original")
        val structured = OfflineHtmlSnapshot.extractMetadata("<script type='application/ld+json'>$json</script>", URI("https://example.com/video"))
        assertEquals("🌊".repeat(250), structured.title)
        assertEquals("Pie original", structured.description)
    }
}
