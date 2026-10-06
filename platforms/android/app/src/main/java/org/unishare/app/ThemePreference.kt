package org.unishare.app

import android.content.Context
import androidx.core.content.edit

enum class ThemePreference(val label: String) {
    SYSTEM("Sistema"),
    LIGHT("Claro"),
    DARK("Oscuro");

    fun next(): ThemePreference = entries[(ordinal + 1) % entries.size]

    companion object {
        fun parse(value: String?): ThemePreference =
            entries.firstOrNull { it.name.equals(value, ignoreCase = true) } ?: SYSTEM
    }
}

object ThemeSettings {
    private const val PREFERENCES = "appearance"
    private const val KEY_THEME = "theme"

    fun load(context: Context): ThemePreference = ThemePreference.parse(
        context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE).getString(KEY_THEME, null),
    )

    fun save(context: Context, value: ThemePreference) {
        context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
            .edit { putString(KEY_THEME, value.name) }
    }
}
