package org.unishare.app

import org.junit.Assert.assertEquals
import org.junit.Test

class ThemePreferenceTest {
    @Test fun cyclesThroughAllChoices() {
        assertEquals(ThemePreference.LIGHT, ThemePreference.SYSTEM.next())
        assertEquals(ThemePreference.DARK, ThemePreference.LIGHT.next())
        assertEquals(ThemePreference.SYSTEM, ThemePreference.DARK.next())
    }

    @Test fun unknownSavedValueFallsBackToSystem() {
        assertEquals(ThemePreference.DARK, ThemePreference.parse("dark"))
        assertEquals(ThemePreference.SYSTEM, ThemePreference.parse("future-value"))
        assertEquals(ThemePreference.SYSTEM, ThemePreference.parse(null))
    }
}
