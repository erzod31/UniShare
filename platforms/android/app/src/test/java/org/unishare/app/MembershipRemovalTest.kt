package org.unishare.app

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class MembershipRemovalTest {
    @Test
    fun `active membership remains active when neither replica removed it`() {
        assertNull(MembershipRemoval.merge(null, null))
    }

    @Test
    fun `observed remote removal tombstones the same membership`() {
        assertEquals("remote", MembershipRemoval.merge(null, "remote"))
    }

    @Test
    fun `existing tombstone cannot be resurrected by an active remote snapshot`() {
        assertEquals("local", MembershipRemoval.merge("local", null))
    }
}
