package org.unishare.app

import org.junit.Assert.assertEquals
import org.junit.Test

class SummaryLinkParserTest {
    @Test
    fun `find detects only web links and trims punctuation`() {
        val links = SummaryLinkParser.find(
            "Documentación https://example.com/guide?q=1. No ftp://example.com/file",
        )

        assertEquals(listOf("https://example.com/guide?q=1"), links.map { it.url })
    }

    @Test
    fun `find keeps balanced parentheses inside link`() {
        val links = SummaryLinkParser.find("Mira (https://example.com/wiki/Function_(math)).")

        assertEquals(listOf("https://example.com/wiki/Function_(math)"), links.map { it.url })
    }

    @Test
    fun `find detects multiple links across lines with exact positions`() {
        val text = "Uno https://one.example/path\nDos http://two.example/path"
        val links = SummaryLinkParser.find(text)

        assertEquals(2, links.size)
        assertEquals(links.map { it.url }, links.map { text.substring(it.start, it.endExclusive) })
    }
}
