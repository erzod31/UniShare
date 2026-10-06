package org.unishare.app

import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class OrganizationNamesTest {
    @Test fun normalizesWhitespaceCaseAndUnicodeWidth() {
        assertEquals(
            OrganizationName("Trabajo importante", "trabajo importante"),
            OrganizationNames.normalize("  Ｔｒａｂａｊｏ   importante  "),
        )
    }

    @Test fun parsesCommaSeparatedNamesAndRemovesDuplicates() {
        assertEquals(
            listOf("Trabajo", "Personal"),
            OrganizationNames.parse("Trabajo, trabajo, Personal").map { it.display },
        )
    }

    @Test fun rejectsEmptyOrOversizedNames() {
        assertThrows(IllegalArgumentException::class.java) { OrganizationNames.normalize("   ") }
        assertThrows(IllegalArgumentException::class.java) { OrganizationNames.normalize("x".repeat(101)) }
    }
}
