package org.unishare.app

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class SharePayloadTest {
    @Test
    fun canonicalizeMatchesDesktopRulesForCommonUrls() {
        assertEquals(
            "https://example.com/path?a=1",
            SharePayload.canonicalize("HTTPS://Example.com:443/path?utm_source=x&a=1#fragment"),
        )
        assertEquals("https://example.com/", SharePayload.canonicalize("https://example.com"))
    }
    @Test
    fun `extracts browser title and url`() {
        assertEquals(
            SharedLink("Interesting page", "https://example.com/article"),
            SharePayload.parse("Interesting page\nhttps://example.com/article"),
        )
    }

    @Test
    fun `derives title for standalone url and keeps exact url`() {
        assertEquals(
            SharedLink("example.com", "https://example.com/path?q=1"),
            SharePayload.parse(" https://example.com/path?q=1 "),
        )
    }

    @Test
    fun `rejects local or credential bearing schemes`() {
        assertNull(SharePayload.parse("file:///tmp/private"))
        assertNull(SharePayload.parse("javascript:alert(1)"))
        assertNull(SharePayload.parse("https://user:password@example.com/private"))
        assertNull(SharePayload.parse("No link here"))
    }

    @Test
    fun `derives a readable automatic title from the host`() {
        assertEquals("example.com", SharePayload.titleFromUrl("https://www.example.com/article"))
    }
}
