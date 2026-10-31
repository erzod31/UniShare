package org.unishare.app

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import java.net.URI
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class OriginMetadataDeviceTest {
    @Test
    fun nativeAndroidJsonPreservesEverySharedCaptionAndItsIdentity() {
        val context = InstrumentationRegistry.getInstrumentation().context
        val cases = context.assets.open("origin-metadata.json").bufferedReader().use {
            JSONObject(it.readText()).getJSONArray("cases")
        }
        for (index in 0 until cases.length()) {
            val fixture = cases.getJSONObject(index)
            val name = fixture.getString("name")
            val lines = fixture.getJSONArray("html")
            val html = (0 until lines.length()).joinToString("\n") { lines.getString(it) }
            val metadata = OfflineHtmlSnapshot.extractMetadata(html, URI(fixture.getString("url")))
            assertEquals(name, if (fixture.isNull("description")) null else fixture.getString("description"), metadata.description)
            if (fixture.has("author")) assertEquals(name, fixture.getString("author"), metadata.author)
            if (fixture.has("expectedTitle")) assertEquals(name, fixture.getString("expectedTitle"), metadata.title)
            if (fixture.has("currentTitle")) assertEquals(name, fixture.getString("resolvedTitle"),
                metadata.resolveTitle(fixture.getString("currentTitle"), fixture.getString("url")))
        }
    }
}
