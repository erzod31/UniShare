package org.unishare.app

import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Test

class UiLanguageTest {
    @After
    fun restoreLanguage() {
        UiStrings.current = UiLanguageChoice.SPANISH
    }

    @Test
    fun englishTranslatesStaticAndDynamicInterfaceText() {
        UiStrings.current = UiLanguageChoice.ENGLISH

        assertEquals("Save", UiStrings.translate("Guardar"))
        assertEquals("Folder: Reading  ×", UiStrings.translate("Carpeta: Reading  ×"))
        assertEquals("Delete (4)", UiStrings.translate("Eliminar (4)"))
        assertEquals("4 items selected", UiStrings.translate("4 elementos seleccionados"))
        assertEquals(
            "Titles and summaries refreshed for 3 links. 1 site did not respond.",
            UiStrings.translate("Títulos y resúmenes actualizados en 3 enlaces. 1 sitios no respondieron."),
        )
        assertEquals(
            "Backup merged: 2 new, 1 updated, and 0 conflicts.",
            UiStrings.translate("Copia incorporada: 2 nuevos, 1 actualizados y 0 conflictos."),
        )
        assertEquals(
            "Synchronized: 1 new, 2 updated and 3 conflicts.",
            UiStrings.translate("Sincronizado: 1 nuevos, 2 actualizados y 3 conflictos."),
        )
    }

    @Test
    fun unknownTextIsPreserved() {
        UiStrings.current = UiLanguageChoice.ENGLISH
        assertEquals("My private title", UiStrings.translate("My private title"))
    }
}
