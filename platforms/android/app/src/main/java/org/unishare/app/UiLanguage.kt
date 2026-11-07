package org.unishare.app

import android.content.Context
import androidx.core.content.edit
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import java.util.Locale

internal enum class UiLanguageChoice {
    SPANISH,
    ENGLISH,
}

internal object UiLanguageSettings {
    private const val PREFERENCES = "ui_language"
    private const val KEY = "choice"

    fun load(context: Context): UiLanguageChoice {
        val saved = context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE).getString(KEY, null)
        return saved?.let { runCatching { UiLanguageChoice.valueOf(it) }.getOrNull() }
            ?: if (Locale.getDefault().language.equals("en", ignoreCase = true)) {
                UiLanguageChoice.ENGLISH
            } else {
                UiLanguageChoice.SPANISH
            }
    }

    fun save(context: Context, choice: UiLanguageChoice) {
        context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
            .edit { putString(KEY, choice.name) }
    }
}

internal object UiStrings {
    var current: UiLanguageChoice by mutableStateOf(UiLanguageChoice.SPANISH)

    private val english = mapOf(
        "Traducir descripciones automáticamente" to "Translate descriptions automatically",
        "Traducción local al idioma de Ajustes. Descarga idiomas al primer uso; el texto no sale del dispositivo." to
            "Local translation into the Settings language. Downloads languages on first use; text stays on your device.",
        "Traduciendo en el dispositivo… La primera vez se descargan los idiomas." to
            "Translating on your device… Languages are downloaded on first use.",
        "Traducción automática local" to "Automatic local translation",
        "Descripción original" to "Original description",
        "Editas la descripción original. La traducción se actualiza al guardar." to
            "You are editing the original description. The translation updates when you save.",
        "Ver original" to "Show original",
        "Ver traducción" to "Show translation",
        "Reintentar traducción" to "Retry translation",
        "Idioma no identificado o no compatible. Se muestra el original." to
            "Language not identified or not supported. Showing the original.",
        "Traducción no disponible. Se muestra el original. Comprueba la conexión para descargar los idiomas." to
            "Translation unavailable. Showing the original. Check your connection to download languages.",
        "Actividad" to "Activity", "Ajustes" to "Settings", "Abrir contenido" to "Open content",
        "Abrir Tailscale" to "Open Tailscale", "Actualizar títulos y resúmenes" to "Refresh titles and summaries",
        "Analizando contenido…" to "Analyzing content…", "Aún no hay carpetas" to "No folders yet",
        "Abrir opciones del elemento" to "Open item options", "Cambiar selección" to "Toggle selection",
        "Autor" to "Author", "Biblioteca" to "Library", "Buscar en tu biblioteca" to "Search your library",
        "Cambiar conexión con Windows" to "Change Windows connection", "Cambios simultáneos" to "Simultaneous changes",
        "Archivo" to "File", "Enlace" to "Link", "Enlace y archivo" to "Link and file",
        "Favorito" to "Favorite", "Disponible sin conexión" to "Available offline", "En la papelera" to "In trash",
        "Seleccionado" to "Selected", "Archivo local disponible" to "Local file available",
        "Sin contenido local" to "No local content", "Seleccionar" to "Select",
        "Cancelar" to "Cancel", "Cargar más" to "Load more", "Carpeta (opcional)" to "Folder (optional)",
        "Clave de emparejamiento" to "Pairing key", "Colecciones" to "Collections",
        "Colecciones separadas por comas" to "Comma-separated collections", "Conecta este teléfono" to "Connect this phone",
        "Conectar con Windows" to "Connect to Windows", "Conectar con Windows…" to "Connect to Windows…",
        "Conectar manualmente" to "Connect manually", "Conservar la mía" to "Keep mine",
        "Copia offline" to "Offline copy", "Datos y dispositivos" to "Data and devices",
        "Dirección HTTPS .ts.net" to "HTTPS .ts.net address", "Editar información" to "Edit information",
        "Editar resumen o notas" to "Edit summary or notes", "Elegir archivo…" to "Choose file…",
        "Eliminar" to "Delete", "Enlace http/https" to "HTTP/HTTPS link",
        "Etiquetas separadas por comas" to "Comma-separated tags", "Exportar copia de seguridad" to "Export backup",
        "Exportar copia de seguridad…" to "Export backup…", "Favoritos" to "Favorites", "Fuente" to "Source",
        "Guardar" to "Save", "Guardar copia offline" to "Save offline copy", "Guardar en UniShare" to "Save to UniShare",
        "Guardar un elemento" to "Save an item", "Guardar y conectar" to "Save and connect", "Guardando…" to "Saving…",
        "Incorporar copia de seguridad" to "Merge backup", "Marcar como favorito" to "Mark as favorite",
        "UniShare verificará la copia y fusionará sus elementos sin borrar los datos actuales. Los cambios simultáneos quedarán como conflictos para que los revises." to
            "UniShare will verify the backup and merge its items without deleting your current data. Concurrent changes will remain as conflicts for you to review.",
        "Más opciones" to "More options", "Modificar resumen" to "Edit summary",
        "No hay elementos para mostrar" to "No items to display", "Organiza sin mover archivos físicos" to "Organize without moving physical files",
        "Sin resumen disponible." to "No summary available.", "Mostrar papelera" to "Show trash",
        "Los cambios se envían al guardarlos." to "Changes are sent when you save them.",
        "Conecta Windows para sincronizar tu biblioteca." to "Connect Windows to sync your library.",
        "Conecta primero este teléfono para recuperar tu biblioteca." to
            "Connect this phone first to retrieve your library.",
        "Reintentar" to "Retry", "Restaurar" to "Restore", "Restaurar copia sin borrar datos" to "Restore backup without deleting data",
        "Restaurar copia sin borrar datos…" to "Restore backup without deleting data…", "Resumen o notas" to "Summary or notes",
        "Revisar" to "Review", "Seleccionar" to "Select", "Sincronización automática" to "Automatic synchronization",
        "Sincronización directa" to "Direct synchronization", "Sincronizar ahora" to "Sync now", "Sincronizando…" to "Syncing…",
        "Solo enlace" to "Link only", "Sólo enlace" to "Link only", "Tema: Sistema" to "Theme: System",
        "Título" to "Title", "Título, URL, notas o etiquetas" to "Title, URL, notes, or tags", "Todos" to "All",
        "Tus elementos" to "Your items", "Usar recibida" to "Use received version", "Verificar e incorporar" to "Verify and merge",
        "Volver a la biblioteca" to "Back to library", "Puedes crear una al guardar o editar un elemento." to
            "You can create one when saving or editing an item.",
        "Se crea automáticamente y puedes cambiarla después." to "It is created automatically and can be changed later.",
        "Conecta Android y Windows a la misma red de Tailscale." to "Connect Android and Windows to the same Tailscale network.",
        "UniShare conservó las dos versiones para evitar pérdida de datos." to
            "UniShare kept both versions to prevent data loss.",
        "Pega un enlace y completaremos automáticamente el título y la información disponible." to
            "Paste a link and we will automatically complete the title and available information.",
        "Lo más sencillo es cerrar esta ventana y escanear el QR que muestra UniShare en Windows." to
            "The easiest option is to close this window and scan the QR shown by UniShare on Windows.",
        "En Windows abre Opciones y herramientas > Conectar teléfono y escanea el QR." to
            "On Windows, open Options and tools > Connect phone and scan the QR.",
        "El contenido compartido no es una URL http/https válida." to
            "The shared content is not a valid HTTP/HTTPS URL.",
        "Enlace compartido guardado en este dispositivo." to "Shared link saved on this device.",
        "No se pudo guardar el enlace compartido." to "The shared link could not be saved.",
        "Teléfono conectado. Descargando tu biblioteca…" to "Phone connected. Downloading your library…",
        "No se pudo leer el QR de emparejamiento." to "The pairing QR code could not be read.",
        "Elemento híbrido guardado localmente." to "Hybrid item saved locally.",
        "Archivo guardado localmente." to "File saved locally.",
        "No se pudo importar el archivo." to "The file could not be imported.",
        "Enlace guardado. Descargando la copia offline…" to "Link saved. Downloading the offline copy…",
        "Enlace y copia offline guardados en el teléfono." to "Link and offline copy saved on the phone.",
        "Enlace guardado con título provisional; no se pudieron consultar sus metadatos." to
            "Link saved with a temporary title; its metadata could not be retrieved.",
        "Enlace guardado localmente." to "Link saved locally.",
        "No se pudo guardar el enlace." to "The link could not be saved.",
        "Ya se están actualizando los títulos y los resúmenes." to
            "Titles and summaries are already being refreshed.",
        "No hay enlaces que actualizar." to "There are no links to refresh.",
        "Los títulos y los resúmenes ya estaban actualizados." to
            "Titles and summaries were already up to date.",
        "Descargando la copia offline…" to "Downloading the offline copy…",
        "Copia offline guardada en el teléfono." to "Offline copy saved on the phone.",
        "No se pudo guardar la copia offline." to "The offline copy could not be saved.",
        "La descarga terminó con un tamaño distinto al anunciado" to
            "The download ended with a different size than announced",
        "No se pudo exportar el respaldo." to "The backup could not be exported.",
        "No se pudo restaurar la copia." to "The backup could not be restored.",
        "No se pudo resolver el conflicto." to "The conflict could not be resolved.",
        "Cambios guardados." to "Changes saved.",
        "Elemento eliminado. Puedes restaurarlo desde Ajustes > Mostrar papelera." to
            "Item deleted. You can restore it from Settings > Show trash.",
        "Elemento restaurado en la biblioteca." to "Item restored to the library.",
        "No se pudo actualizar el elemento." to "The item could not be updated.",
        "Este elemento no tiene contenido disponible" to "This item has no available content",
        "No se pudo abrir el elemento." to "The item could not be opened.",
        "No se pudo abrir el enlace." to "The link could not be opened.",
        "No hay una aplicación instalada que pueda abrir este contenido." to
            "No installed application can open this content.",
        "No hay una aplicación instalada que pueda abrir este enlace." to
            "No installed application can open this link.",
        "Configuración guardada. Conectando con el ordenador…" to
            "Settings saved. Connecting to the computer…",
        "Configuración de sincronización no válida." to "Invalid synchronization settings.",
        "Permiso de red local concedido." to "Local network permission granted.",
        "UniShare necesita permiso de red local para sincronizar directamente." to
            "UniShare needs local network permission to synchronize directly.",
        "Sin permiso de red local, Android no puede sincronizar directamente." to
            "Without local network permission, Android cannot synchronize directly.",
        "Sincronización pendiente: UniShare necesita permiso de red local para sincronizar directamente." to
            "Synchronization pending: UniShare needs local network permission to synchronize directly.",
        "Sincronización pendiente: Sin permiso de red local, Android no puede sincronizar directamente." to
            "Synchronization pending: Without local network permission, Android cannot synchronize directly.",
        "Abre Tailscale manualmente y comprueba que la conexión y su DNS estén activados." to
            "Open Tailscale manually and check that the connection and its DNS are enabled.",
        "Usa un enlace http o https válido para asociarlo al archivo." to
            "Use a valid HTTP or HTTPS link to associate it with the file.",
        "Sincronizado" to "Synchronized", "Sincronización pendiente" to "Synchronization pending",
        "¿Eliminar este elemento?" to "Delete this item?",
        "Vaciar papelera" to "Empty trash",
        "Eliminar definitivamente" to "Delete permanently",
        "¿Vaciar la papelera definitivamente?" to "Permanently empty trash?",
        "Esta acción eliminará definitivamente los elementos de la Papelera en este dispositivo y liberará espacio." to
            "This action will permanently delete the items in Trash on this device and free up space.",
        "La papelera se ha vaciado definitivamente." to "Trash has been permanently emptied.",
        "Organizar" to "Organize",
        "Organizar selección" to "Organize selection",
        "Añadir a carpetas (separadas por comas)" to "Add to folders (comma-separated)",
        "Añadir etiquetas (separadas por comas)" to "Add tags (comma-separated)",
        "Aplicar" to "Apply",
        "Apariencia e idioma" to "Appearance and language",
        "Papelera" to "Trash",
        "Sincronización directa por Tailscale" to "Direct synchronization via Tailscale",
        "Analiza enlaces para completar su información" to "Analyze links to complete their information",
        "Guarda tu biblioteca en un archivo .unishare.zip" to "Save your library to a .unishare.zip file",
        "Fusiona elementos verificados sin perder cambios" to "Merge verified items without losing changes",
        "Toca para alternar entre Sistema, Claro y Oscuro" to "Tap to cycle between System, Light, and Dark",
        "Idioma de la interfaz y de la traducción local" to "Interface and local translation language",
        "Elementos eliminados que puedes restaurar o vaciar" to "Deleted items you can restore or permanently empty",
        "Abrir Ajustes y actualizaciones…" to "Open Settings and updates…",
        "Cambiar" to "Change",
    )

    fun translate(value: String): String {
        if (current == UiLanguageChoice.SPANISH) return value
        english[value]?.let { return it }
        return value
            .replace(Regex("^Carpeta: (.+)  ×$"), "Folder: $1  ×")
            .replace(Regex("^Eliminar \\((\\d+)\\)$"), "Delete ($1)")
            .replace(Regex("^Restaurar \\((\\d+)\\)$"), "Restore ($1)")
            .replace(Regex("^Eliminar definitivamente \\((\\d+)\\)$"), "Delete permanently ($1)")
            .replace(Regex("^Organizar \\((\\d+)\\)$"), "Organize ($1)")
            .replace(Regex("^(\\d+) elementos eliminados definitivamente\\.$"), "$1 items permanently deleted.")
            .replace(Regex("^1 elemento eliminado definitivamente\\.$"), "1 item permanently deleted.")
            .replace(Regex("^Notas \\(mía\\): (.+)$"), "Notes (mine): $1")
            .replace(Regex("^Notas \\(recibida\\): (.+)$"), "Notes (received): $1")
            .replace(Regex("^Resolver conflictos \\((\\d+)\\)$"), "Resolve conflicts ($1)")
            .replace(Regex("^(\\d+) por revisar$"), "$1 to review")
            .replace(Regex("^(\\d+) elementos seleccionados$"), "$1 items selected")
            .replace(Regex("^(\\d+) seleccionados$"), "$1 selected")
            .replace(Regex("^(\\d+) elementos$"), "$1 items")
            .replace(Regex("^1 elemento$"), "1 item")
            .replace(Regex("^Tema: Claro$"), "Theme: Light")
            .replace(Regex("^Tema: Oscuro$"), "Theme: Dark")
            .replace(Regex("^Tema: Sistema$"), "Theme: System")
            .replace(Regex("^¿Eliminar (\\d+) elementos\\?$"), "Delete $1 items?")
            .replace(Regex("^Mi versión: (.+)$"), "My version: $1")
            .replace(Regex("^Versión recibida: (.+)$"), "Received version: $1")
            .replace(Regex("^Analizando el contenido de (\\d+) enlaces…$"), "Analyzing the content of $1 links…")
            .replace(
                Regex("^Títulos y resúmenes actualizados en (\\d+) enlaces\\.(?: (\\d+) sitios no respondieron\\.)?$"),
            ) { match ->
                "Titles and summaries refreshed for ${match.groupValues[1]} links." +
                    if (match.groupValues[2].isNotEmpty()) {
                        val count = match.groupValues[2]
                        " $count ${if (count == "1") "site" else "sites"} did not respond."
                    } else ""
            }
            .replace(Regex("^No se pudo consultar la información de (\\d+) sitios\\.$"),
                "Could not retrieve information from $1 sites.")
            .replace(Regex("^Paquete listo: (\\d+) elementos y (\\d+) archivos\\.$"),
                "Package ready: $1 items and $2 files.")
            .replace(
                Regex("^Copia incorporada: (\\d+) nuevos, (\\d+) actualizados y (\\d+) conflictos\\.$"),
                "Backup merged: $1 new, $2 updated, and $3 conflicts.",
            )
            .replace(Regex("^(\\d+) elementos eliminados\\. Puedes restaurarlos desde Ajustes > Mostrar papelera\\.$"),
                "$1 items deleted. You can restore them from Settings > Show trash.")
            .replace(Regex("^(\\d+) elementos restaurados en la biblioteca\\.$"),
                "$1 items restored to the library.")
            .replace(
                Regex("^Sincronizado: (\\d+) nuevos, (\\d+) actualizados(?: y (\\d+) conflictos)?\\.$"),
            ) { match ->
                "Synchronized: ${match.groupValues[1]} new, ${match.groupValues[2]} updated" +
                    if (match.groupValues[3].isNotEmpty()) " and ${match.groupValues[3]} conflicts." else "."
            }
            .replace(Regex("^Sincronización pendiente: (.+)$"), "Synchronization pending: $1")
            .replace(
                "pendiente: UniShare necesita permiso de red local para sincronizar directamente.",
                "pending: UniShare needs local network permission to synchronize directly.",
            )
            .replace(
                "pendiente: Sin permiso de red local, Android no puede sincronizar directamente.",
                "pending: Without local network permission, Android cannot synchronize directly.",
            )
    }
}
