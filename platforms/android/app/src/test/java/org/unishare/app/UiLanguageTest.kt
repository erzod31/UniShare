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
        assertEquals("The download ended with a different size than announced",
            UiStrings.translate("La descarga terminó con un tamaño distinto al anunciado"))
        assertEquals("Connect this phone first to retrieve your library.",
            UiStrings.translate("Conecta primero este teléfono para recuperar tu biblioteca."))
        assertEquals(
            "Synchronization pending: UniShare needs local network permission to synchronize directly.",
            UiStrings.translate(
                "Sincronización pendiente: UniShare necesita permiso de red local para sincronizar directamente.",
            ),
        )
        assertEquals(
            "2026-10-15T10:00:00Z: pending: UniShare needs local network permission to synchronize directly.",
            UiStrings.translate(
                "2026-10-15T10:00:00Z: pendiente: UniShare necesita permiso de red local para sincronizar directamente.",
            ),
        )
        assertEquals("Folder: Reading  ×", UiStrings.translate("Carpeta: Reading  ×"))
        assertEquals("Delete (4)", UiStrings.translate("Eliminar (4)"))
        assertEquals("Empty trash", UiStrings.translate("Vaciar papelera"))
        assertEquals("Delete permanently (3)", UiStrings.translate("Eliminar definitivamente (3)"))
        assertEquals("Organize (2)", UiStrings.translate("Organizar (2)"))
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
