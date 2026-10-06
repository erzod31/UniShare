package org.unishare.app

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotEquals
import org.junit.Assert.assertThrows
import org.junit.Test
import java.util.UUID

class PortablePackageIdentityTest {
    @Test fun preservesExplicitSourceDeviceIdentity() {
        val expected = "d5136466-ae82-4f29-974f-e1cc760013bf"

        assertEquals(
            expected,
            PortablePackageIdentity.sourceDeviceId(expected),
        )
    }

    @Test fun assignsStableIdentityToLegacyBackups() {
        val first = PortablePackageIdentity.sourceDeviceId(null)
        val second = PortablePackageIdentity.sourceDeviceId("")

        assertEquals(first, second)
        assertEquals(first, UUID.fromString(first).toString())
        assertNotEquals("00000000-0000-0000-0000-000000000000", first)
    }

    @Test fun rejectsMalformedExplicitIdentity() {
        assertThrows(IllegalArgumentException::class.java) {
            PortablePackageIdentity.sourceDeviceId("not-a-uuid")
        }
    }
}
