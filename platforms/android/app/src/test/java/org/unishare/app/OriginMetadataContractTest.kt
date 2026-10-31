package org.unishare.app

import java.net.URI
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith
import org.junit.runners.Parameterized

@RunWith(Parameterized::class)
class OriginMetadataContractTest(private val name: String, private val fixture: JSONObject) {
    @Test
    fun originalContentMatchesTheSharedPlatformContract() {
        val html = fixture.getJSONArray("html").let { lines ->
            (0 until lines.length()).joinToString("\n") { lines.getString(it) }
        }
        val metadata = OfflineHtmlSnapshot.extractMetadata(html, URI(fixture.getString("url")))
        assertEquals(name, if (fixture.isNull("description")) null else fixture.getString("description"), metadata.description)
        if (fixture.has("author")) assertEquals(name, fixture.getString("author"), metadata.author)
        if (fixture.has("expectedTitle")) assertEquals(name, fixture.getString("expectedTitle"), metadata.title)
        if (fixture.has("currentTitle")) assertEquals(name, fixture.getString("resolvedTitle"),
            metadata.resolveTitle(fixture.getString("currentTitle"), fixture.getString("url")))
        assertEquals(name, "Mi nota personal", metadata.resolveDescription("Mi nota personal"))
    }

    companion object {
        @JvmStatic
        @Parameterized.Parameters(name = "{0}")
        fun cases(): Collection<Array<Any>> {
            val stream = checkNotNull(OriginMetadataContractTest::class.java.classLoader
                ?.getResourceAsStream("origin-metadata.json"))
            val cases = stream.bufferedReader().use { JSONObject(it.readText()).getJSONArray("cases") }
            return (0 until cases.length()).map { index ->
                val fixture = cases.getJSONObject(index)
                arrayOf(fixture.getString("name"), fixture)
            }
        }
    }
}
