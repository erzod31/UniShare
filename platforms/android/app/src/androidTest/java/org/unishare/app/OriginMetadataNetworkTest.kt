package org.unishare.app

import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import java.net.URI
import java.security.MessageDigest
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith

/** Explicit network probe; not part of the offline device suite. Requires a public TikTok test URL. */
@RunWith(AndroidJUnit4::class)
class OriginMetadataNetworkTest {
    @Test
    fun publicPostCaptionMatchesTheOriginalProviderPayload() {
        val instrumentation = InstrumentationRegistry.getInstrumentation()
        val arguments = InstrumentationRegistry.getArguments()
        val url = checkNotNull(arguments.getString("metadataUrl"))
        val expected = checkNotNull(arguments.getString("expectedCaptionSha256"))
        require(OriginContentRules.tikTokPostId(URI(url)) != null)
        val metadata = checkNotNull(OfflinePageDownloader(instrumentation.targetContext).fetchMetadata(url))
        val caption = checkNotNull(metadata.description)
        val sha256 = MessageDigest.getInstance("SHA-256").digest(caption.toByteArray())
            .joinToString("") { "%02x".format(it.toInt() and 0xff) }
        assertEquals(expected.lowercase(), sha256)
        assertTrue(metadata.originDescriptionFound)
        assertFalse(metadata.author.isNullOrBlank())
        assertEquals("Mi nota personal", metadata.resolveDescription("Mi nota personal"))
        instrumentation.sendStatus(0, android.os.Bundle().apply {
            putInt("caption_characters", caption.length)
            putString("caption_sha256", sha256)
        })
    }
}
