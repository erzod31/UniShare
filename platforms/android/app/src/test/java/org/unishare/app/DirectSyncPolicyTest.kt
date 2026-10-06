package org.unishare.app

import org.junit.Assert.assertFalse
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assert.assertThrows
import org.junit.Test
import java.net.UnknownHostException

class DirectSyncPolicyTest {
    @Test fun firstCheckTransfers() =
        assertTrue(DirectSyncPolicy.shouldTransfer(false, "server", null, false))

    @Test fun unchangedPeriodicCheckDoesNotTransfer() =
        assertFalse(DirectSyncPolicy.shouldTransfer(false, "same", "same", false))

    @Test fun unchangedServerWithoutLocalChangesIsVerifiedUpToDate() =
        assertTrue(DirectSyncPolicy.isUpToDate("same", "same", false))

    @Test fun failedFirstCheckCannotBeReportedAsUpToDate() =
        assertFalse(DirectSyncPolicy.isUpToDate(null, null, false))

    @Test fun pendingLocalChangesCannotBeReportedAsUpToDate() =
        assertFalse(DirectSyncPolicy.isUpToDate("same", "same", true))

    @Test fun changedServerTransfers() =
        assertTrue(DirectSyncPolicy.shouldTransfer(false, "new", "old", false))

    @Test fun durableLocalRetryAlwaysTransfers() =
        assertTrue(DirectSyncPolicy.shouldTransfer(true, null, "same", false))

    @Test fun unacknowledgedOperationCounterRemainsPending() {
        assertTrue(DirectSyncCursorPolicy.hasPending(8, 7))
        assertFalse(DirectSyncCursorPolicy.hasPending(8, 8))
    }

    @Test fun missingOrFutureAcknowledgementForcesFullUpload() {
        assertTrue(DirectSyncCursorPolicy.requiresFullUpload(true, 8, null))
        assertTrue(DirectSyncCursorPolicy.requiresFullUpload(true, 8, 9))
        assertFalse(DirectSyncCursorPolicy.requiresFullUpload(true, 8, 7))
    }

    @Test fun causalResponseRequiresMatchingIdentityAndAcknowledgement() {
        assertTrue(DirectSyncCursorPolicy.responseIsCausallyValid(
            "server", "server", 5, 6, 8, 8, false,
        ))
        assertFalse(DirectSyncCursorPolicy.responseIsCausallyValid(
            "server", "other", 5, 6, 8, 8, false,
        ))
        assertFalse(DirectSyncCursorPolicy.responseIsCausallyValid(
            "server", "server", 5, 6, 8, 7, false,
        ))
    }

    @Test fun fullSnapshotCanRecoverFromServerCursorRollback() {
        assertTrue(DirectSyncCursorPolicy.responseIsCausallyValid(
            "server", "server", 9, 3, 8, 8, true,
        ))
        assertFalse(DirectSyncCursorPolicy.responseIsCausallyValid(
            "server", "server", 9, 3, 8, 8, false,
        ))
    }

    @Test
    fun `local changes force transfer even when server revision is unchanged`() {
        assertTrue(DirectSyncPolicy.shouldTransfer(false, "same", "same", true))
    }

    @Test fun pairingQrDecodesEndpointAndKey() {
        val key = "abcdefghijklmnopqrstuvwxyz0123456789-_"
        val settings = DirectSyncPairing.parse(
            "unishare://pair?endpoint=https%3A%2F%2Fcomputer.example.ts.net%3A8443&key=$key",
        )
        assertEquals("https://computer.example.ts.net:8443", settings.endpoint)
        assertEquals(key, settings.pairingKey)
    }

    @Test fun emulatorLoopbackIsAcceptedOnlyInDebugBuilds() {
        val key = "abcdefghijklmnopqrstuvwxyz0123456789-_"
        val link = "unishare://pair?endpoint=http%3A%2F%2F10.0.2.2%3A47832&key=$key"
        if (BuildConfig.DEBUG) {
            assertEquals("http://10.0.2.2:47832", DirectSyncPairing.parse(link).endpoint)
        } else {
            assertThrows(IllegalArgumentException::class.java) { DirectSyncPairing.parse(link) }
        }
    }

    @Test fun pairingQrRejectsNonTailscaleEndpoints() {
        val key = "abcdefghijklmnopqrstuvwxyz0123456789-_"
        assertThrows(IllegalArgumentException::class.java) {
            DirectSyncPairing.parse("unishare://pair?endpoint=https%3A%2F%2Fexample.com&key=$key")
        }
    }

    @Test fun pairingQrRejectsIncompletePayloads() {
        assertThrows(IllegalArgumentException::class.java) {
            DirectSyncPairing.parse("unishare://pair?endpoint=https%3A%2F%2Fcomputer.example.ts.net")
        }
    }

    @Test fun dnsFailuresExplainHowToRestoreTailscaleResolution() {
        val message = DirectSyncErrorMessages.forUser(
            IllegalStateException("falló", UnknownHostException("computer.example.ts.net")),
        )
        assertTrue(message.contains("Usar DNS de Tailscale"))
        assertTrue(message.contains("túnel dividido"))
    }
}
