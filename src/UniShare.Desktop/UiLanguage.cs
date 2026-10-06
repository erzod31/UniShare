using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace UniShare.Desktop;

public enum UiLanguageChoice
{
    Spanish,
    English,
}

public static partial class UiLanguage
{
    private static readonly Dictionary<string, string> English =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Abrir"] = "Open",
            ["Abrir UniShare"] = "Open UniShare",
            ["Abrir contenido"] = "Open content",
            ["Abrir Tailscale"] = "Open Tailscale",
            ["Activa en segundo plano"] = "Active in the background",
            ["Actividad"] = "Activity",
            ["Actualizado"] = "Updated",
            ["Actualizar"] = "Refresh",
            ["Actualizar títulos y resúmenes"] = "Refresh titles and summaries",
            ["Ajustes"] = "Settings",
            ["Analizando contenido…"] = "Analyzing content…",
            ["Aquí podrás abrirlo, editarlo o guardarlo offline."] = "You can open, edit, or save it offline here.",
            ["Archivo"] = "File",
            ["Archivo…"] = "File…",
            ["Archivos"] = "Files",
            ["Autor"] = "Author",
            ["Biblioteca"] = "Library",
            ["Biblioteca personal"] = "Personal library",
            ["Buscar"] = "Search",
            ["Buscar en la biblioteca"] = "Search the library",
            ["Buscar en tu biblioteca"] = "Search your library",
            ["Cambiar biblioteca…"] = "Change library…",
            ["Cancelar"] = "Cancel",
            ["Cargar más"] = "Load more",
            ["Carpeta (opcional)"] = "Folder (optional)",
            ["CARPETAS"] = "FOLDERS",
            ["Cerrar"] = "Close",
            ["Clave de emparejamiento"] = "Pairing key",
            ["Clave privada"] = "Private key",
            ["Colecciones"] = "Collections",
            ["Colecciones…"] = "Collections…",
            ["Cola de descargas"] = "Download queue",
            ["Comprobando…"] = "Checking…",
            ["Con archivos"] = "With files",
            ["Conectar con Windows"] = "Connect to Windows",
            ["Conectar con Windows…"] = "Connect to Windows…",
            ["Conectar dispositivos"] = "Connect devices",
            ["Conectar dispositivos y Edge"] = "Connect devices and Edge",
            ["Conectar manualmente"] = "Connect manually",
            ["Conectar teléfono…"] = "Connect phone…",
            ["Conflictos"] = "Conflicts",
            ["Conservar mi versión"] = "Keep my version",
            ["Conservar la mía"] = "Keep mine",
            ["Copia offline"] = "Offline copy",
            ["Copiar…"] = "Copy…",
            ["Crear un acceso directo en el escritorio"] = "Create a desktop shortcut",
            ["Crea una carpeta al guardar o editar un elemento."] = "Create a folder when saving or editing an item.",
            ["Datos y dispositivos"] = "Data and devices",
            ["Descargas"] = "Downloads",
            ["Dirección HTTPS .ts.net"] = "HTTPS .ts.net address",
            ["Editar"] = "Edit",
            ["Editar elemento"] = "Edit item",
            ["Editar información"] = "Edit information",
            ["Editar metadatos"] = "Edit metadata",
            ["Editar resumen o notas"] = "Edit summary or notes",
            ["Elegir archivo…"] = "Choose file…",
            ["Eliminar"] = "Delete",
            ["En curso"] = "In progress",
            ["Enlace"] = "Link",
            ["Enlaces"] = "Links",
            ["Enlace http/https"] = "HTTP/HTTPS link",
            ["Error"] = "Error",
            ["Estado"] = "Status",
            ["Etiquetas…"] = "Tags…",
            ["Exportar…"] = "Export…",
            ["Exportar a Obsidian…"] = "Export to Obsidian…",
            ["Exportar copia de seguridad"] = "Export backup",
            ["Exportar copia de seguridad…"] = "Export backup…",
            ["Favorito"] = "Favorite",
            ["Favoritos"] = "Favorites",
            ["Fuente"] = "Source",
            ["Formulario para guardar en UniShare"] = "Form to save to UniShare",
            ["Guardar"] = "Save",
            ["Guardar cambios"] = "Save changes",
            ["Guardar copia offline"] = "Save offline copy",
            ["Guardar en UniShare"] = "Save to UniShare",
            ["Guardar un elemento"] = "Save an item",
            ["Guardando…"] = "Saving…",
            ["Híbrido"] = "Hybrid",
            ["Híbridos"] = "Hybrid",
            ["Elemento"] = "Item",
            ["Enlace y archivo"] = "Link and file",
            ["En la papelera"] = "In trash",
            ["Disponible offline"] = "Available offline",
            ["Sin resumen disponible. Puedes añadir notas desde Editar información."] =
                "No summary available. You can add notes from Edit information.",
            ["★ Favorito"] = "★ Favorite",
            ["Idioma: Español"] = "Language: English",
            ["Importar…"] = "Import…",
            ["Importar notas desde Obsidian…"] = "Import notes from Obsidian…",
            ["Iniciar con Windows"] = "Start with Windows",
            ["Marcar como favorito"] = "Mark as favorite",
            ["Más opciones"] = "More options",
            ["Modificar resumen"] = "Edit summary",
            ["Mostrar toda la biblioteca"] = "Show the entire library",
            ["Nodo local activo"] = "Local node active",
            ["No hay copias offline"] = "There are no offline copies",
            ["No hay elementos para mostrar"] = "No items to display",
            ["No hay resultados"] = "No results",
            ["No marcado como favorito"] = "Not marked as favorite",
            ["No tienes favoritos"] = "You have no favorites",
            ["Normal"] = "Normal",
            ["Opciones y herramientas"] = "Options and tools",
            ["Organiza sin mover archivos físicos"] = "Organize without moving physical files",
            ["Papelera"] = "Trash",
            ["Preparado."] = "Ready.",
            ["Reintentar"] = "Retry",
            ["Requiere revisión"] = "Needs attention",
            ["Resolver conflictos…"] = "Resolve conflicts…",
            ["Restaurar"] = "Restore",
            ["Restaurar copia…"] = "Restore backup…",
            ["Restaurar copia sin borrar datos"] = "Restore backup without deleting data",
            ["Restaurar copia sin borrar datos…"] = "Restore backup without deleting data…",
            ["Resumen / notas"] = "Summary / notes",
            ["Resumen o notas"] = "Summary or notes",
            ["Salir completamente"] = "Exit completely",
            ["Selecciona un elemento"] = "Select an item",
            ["Seleccionar"] = "Select",
            ["Sin archivos"] = "Without files",
            ["Sin conexión"] = "Offline",
            ["Sincronización automática"] = "Automatic synchronization",
            ["Sincronización directa"] = "Direct synchronization",
            ["Sincronizar ahora"] = "Sync now",
            ["Sincronizando…"] = "Syncing…",
            ["Sólo enlace"] = "Link only",
            ["Solo enlace"] = "Link only",
            ["Tamaño del texto: Normal"] = "Text size: Normal",
            ["Tema: Sistema"] = "Theme: System",
            ["Tipo"] = "Type",
            ["Título"] = "Title",
            ["Título, URL, autor, notas o etiquetas"] = "Title, URL, author, notes, or tags",
            ["Título, URL, notas o etiquetas"] = "Title, URL, notes, or tags",
            ["Todos"] = "All",
            ["Todos los elementos"] = "All items",
            ["Tu biblioteca está vacía"] = "Your library is empty",
            ["Tus elementos"] = "Your items",
            ["Usar la versión recibida"] = "Use received version",
            ["Usar recibida"] = "Use received version",
            ["Ver descargas pendientes"] = "View pending downloads",
            ["Verificar e incorporar"] = "Verify and merge",
            ["Volver a la biblioteca"] = "Back to library",
            ["＋  Guardar"] = "＋  Save",
            ["Guarda un enlace o arrastra archivos aquí."] = "Save a link or drag files here.",
            ["Marca un elemento como favorito desde Editar información."] = "Mark an item as a favorite from Edit information.",
            ["Selecciona un enlace y guarda una copia offline."] = "Select a link and save an offline copy.",
            ["Los elementos que elimines aparecerán aquí y podrás restaurarlos."] = "Deleted items will appear here and can be restored.",
            ["Prueba con otros términos o borra la búsqueda."] = "Try other terms or clear the search.",
            ["No hay elementos seleccionados."] = "No items selected.",
            ["La papelera está vacía"] = "The trash is empty",
            ["UniShare sigue sincronizando"] = "UniShare is still syncing",
            ["La ventana se ocultó. Usa el icono junto al reloj para abrirla o salir completamente."] =
                "The window was hidden. Use the tray icon to open it or exit completely.",
            ["UniShare - sincronización activa"] = "UniShare - synchronization active",
            ["Esta biblioteca ya está abierta."] = "This library is already open.",
            ["Elemento guardado localmente."] = "Item saved locally.",
            ["Verificando y restaurando la copia..."] = "Verifying and restoring the backup...",
            ["Selecciona un enlace para descargar."] = "Select a link to download.",
            ["Descargando y verificando contenido..."] = "Downloading and verifying content...",
            ["Selecciona uno o más elementos de la biblioteca."] = "Select one or more library items.",
            ["Selecciona uno o más elementos de la Papelera."] = "Select one or more items from Trash.",
            ["Selecciona un elemento."] = "Select an item.",
            ["Cambios guardados."] = "Changes saved.",
            ["Contenido abierto con la aplicación predeterminada."] = "Content opened with the default application.",
            ["Ya se están actualizando los títulos y los resúmenes."] = "Titles and summaries are already being refreshed.",
            ["No hay enlaces que actualizar."] = "There are no links to refresh.",
            ["Los títulos y los resúmenes ya estaban actualizados."] = "Titles and summaries were already up to date.",
            ["Selecciona primero un conflicto."] = "Select a conflict first.",
            ["No hay conflictos pendientes."] = "There are no pending conflicts.",
            ["Se aplicó la versión recibida."] = "The received version was applied.",
            ["Se conservó tu versión."] = "Your version was kept.",
            ["Selecciona una descarga fallida o pausada."] = "Select a failed or paused download.",
            ["Selecciona una descarga."] = "Select a download.",
            ["Descargando..."] = "Downloading...",
            ["Escribe un título."] = "Enter a title.",
            ["Indica un enlace, un archivo o ambos."] = "Provide a link, a file, or both.",
            ["Puerto local copiado."] = "Local port copied.",
            ["Clave privada copiada. Pégala en Edge y evita compartirla."] =
                "Private key copied. Paste it into Edge and do not share it.",
            ["Dirección guardada. Ya puedes escanear el QR."] = "Address saved. You can now scan the QR.",
            ["Biblioteca no válida"] = "Invalid library",
            ["Elegir biblioteca"] = "Choose library",
            ["Pega un enlace completo que empiece por http:// o https://."] =
                "Paste a complete link beginning with http:// or https://.",
            ["Copia offline pendiente"] = "Offline copy pending",
            ["Restaurar copia"] = "Restore backup",
            ["Restauración completada"] = "Restore completed",
            ["Importar cambios de Obsidian"] = "Import Obsidian changes",
            ["Importar elementos"] = "Import items",
            ["Resultado de la importación"] = "Import result",
            ["Eliminar elemento"] = "Delete item",
            ["Eliminar elementos"] = "Delete items",
            ["Elemento eliminado. Puedes restaurarlo desde la Papelera."] = "Item deleted. You can restore it from Trash.",
            ["Elemento restaurado en la biblioteca."] = "Item restored to the library.",
            ["Este elemento no tiene contenido disponible para abrir."] = "This item has no content available to open.",
            ["Elige la carpeta raíz de tu biblioteca UniShare"] = "Choose the root folder for your UniShare library",
            ["Respaldo de UniShare (*.unishare.zip)|*.unishare.zip|Archivo ZIP (*.zip)|*.zip"] =
                "UniShare backup (*.unishare.zip)|*.unishare.zip|ZIP file (*.zip)|*.zip",
            ["Exportar respaldo de UniShare"] = "Export UniShare backup",
            ["Selecciona la copia de seguridad de UniShare"] = "Select the UniShare backup",
            ["Elige dónde crear la biblioteca restaurada"] = "Choose where to create the restored library",
            ["Elige la carpeta raíz de tu bóveda de Obsidian"] = "Choose the root folder of your Obsidian vault",
            ["Elige la bóveda de Obsidian exportada por UniShare"] = "Choose the Obsidian vault exported by UniShare",
            ["1. Conecta ambos dispositivos"] = "1. Connect both devices",
            ["2. Activa el acceso privado de UniShare"] = "2. Enable private UniShare access",
            ["3. Escanea una sola vez"] = "3. Scan once",
            ["Archivo (opcional si indicas un enlace)"] = "File (optional if you provide a link)",
            ["Archivo seleccionado"] = "Selected file",
            ["Arrastra archivos o enlaces para guardarlos"] = "Drag files or links here to save them",
            ["Cambios que necesitan tu decisión"] = "Changes that need your decision",
            ["Carpeta opcional dentro de UniShare"] = "Optional folder in UniShare",
            ["Colecciones (separadas por comas)"] = "Collections (comma-separated)",
            ["Colecciones separadas por comas"] = "Comma-separated collections",
            ["Conflictos de sincronización"] = "Synchronization conflicts",
            ["Copia estos datos en las opciones de la extensión UniShare. La clave sólo se muestra en este equipo."] =
                "Copy these details into the UniShare extension options. The key is only shown on this computer.",
            ["Copiar clave"] = "Copy key",
            ["Copiar comando"] = "Copy command",
            ["Copiar enlace de emparejamiento"] = "Copy pairing link",
            ["Copiar puerto"] = "Copy port",
            ["Datos de conexión para Edge"] = "Connection details for Edge",
            ["Descargas de UniShare"] = "UniShare downloads",
            ["Después de emparejar, UniShare sincroniza al guardar, al abrir la app y periódicamente cuando Android la mantiene cerrada. Si un dispositivo no está disponible, conserva el cambio y lo reintenta."] =
                "After pairing, UniShare syncs when saving, when the app opens, and periodically while Android keeps it closed. If a device is unavailable, it keeps the change and retries.",
            ["Detectado"] = "Detected",
            ["Ejecuta una vez este comando en PowerShell:"] = "Run this command once in PowerShell:",
            ["Ejemplo: https://ejemplo.com/artículo"] = "Example: https://example.com/article",
            ["El QR contiene la clave privada de tu biblioteca. Escanéalo sólo con tu teléfono y no lo compartas."] =
                "The QR contains your library's private key. Scan it only with your phone and do not share it.",
            ["Elementos de la biblioteca"] = "Library items",
            ["Enlace (opcional si eliges un archivo)"] = "Link (optional if you choose a file)",
            ["Enlace HTTP o HTTPS"] = "HTTP or HTTPS link",
            ["Etiquetas (separadas por comas)"] = "Tags (comma-separated)",
            ["Etiquetas separadas por comas"] = "Comma-separated tags",
            ["Examinar..."] = "Browse...",
            ["Guardar el primer elemento"] = "Save the first item",
            ["Guardar elemento"] = "Save item",
            ["Guardar y crear QR"] = "Save and create QR",
            ["Instala la versión actual de UniShare en Android y escanea este QR con la cámara. La app se abrirá, guardará la conexión y descargará la biblioteca automáticamente."] =
                "Install the current UniShare version on Android and scan this QR with the camera. The app will open, save the connection, and download the library automatically.",
            ["Instala Tailscale en Windows y Android e inicia sesión en la misma red privada. No abras puertos del router."] =
                "Install Tailscale on Windows and Android and sign in to the same private network. Do not open router ports.",
            ["Las transferencias interrumpidas pueden reanudarse si el servidor conserva el mismo recurso."] =
                "Interrupted transfers can resume if the server keeps the same resource.",
            ["Mi versión"] = "My version",
            ["Mueve el elemento a la Papelera; se puede restaurar."] =
                "Moves the item to Trash; it can be restored.",
            ["No compartas esta clave: permite guardar y sincronizar contenido con tu biblioteca."] =
                "Do not share this key: it allows saving and syncing content with your library.",
            ["Origen"] = "Origin",
            ["Pega aquí la dirección HTTPS que muestra Tailscale (termina en .ts.net:8443):"] =
                "Paste the HTTPS address shown by Tailscale here (ending in .ts.net:8443):",
            ["Pega un enlace; UniShare completa el título y la información disponible."] =
                "Paste a link; UniShare completes the title and available information.",
            ["Progreso"] = "Progress",
            ["Puerto local"] = "Local port",
            ["Resumen o notas en modo lectura"] = "Read-only summary or notes",
            ["Se crea automáticamente"] = "Created automatically",
            ["Título *"] = "Title *",
            ["Versión recibida"] = "Received version",
            ["UniShare no sobrescribe ediciones simultáneas. Selecciona un conflicto y elige la versión que quieres conservar."] =
                "UniShare does not overwrite simultaneous edits. Select a conflict and choose the version you want to keep.",
            ["Ctrl+L guarda un enlace, Ctrl+F busca, Ctrl+N añade un elemento, Ctrl+E edita y Supr elimina la selección."] =
                "Ctrl+L saves a link, Ctrl+F searches, Ctrl+N adds an item, Ctrl+E edits, and Delete removes the selection.",
        };

    internal static UiLanguageChoice Choice { get; private set; } = UiLanguageChoice.Spanish;
    internal static string Label => Choice == UiLanguageChoice.English ? "English" : "Español";

    internal static void Initialize()
    {
        var path = ConfigurationPath();
        var saved = File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        Choice = Enum.TryParse<UiLanguageChoice>(saved, true, out var parsed)
            ? parsed
            : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase)
                ? UiLanguageChoice.English
                : UiLanguageChoice.Spanish;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnElementLoaded));
    }

    internal static void ToggleAndSave()
    {
        Choice = Choice == UiLanguageChoice.Spanish ? UiLanguageChoice.English : UiLanguageChoice.Spanish;
        var path = ConfigurationPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var partial = path + ".partial";
        File.WriteAllText(partial, Choice.ToString());
        File.Move(partial, path, true);
    }

    internal static string T(string? value) => Translate(value, Choice);

    public static string Translate(string? value, UiLanguageChoice choice)
    {
        if (string.IsNullOrEmpty(value) || choice == UiLanguageChoice.Spanish)
        {
            return value ?? string.Empty;
        }
        if (English.TryGetValue(value, out var translated))
        {
            return translated;
        }

        var result = value;
        result = Regex.Replace(result, @"^Carpeta: (.+)$", "Folder: $1");
        result = Regex.Replace(result, @"^(\d+) elementos seleccionados\. Eliminar y restaurar se aplicarán al grupo\.$",
            "$1 items selected. Delete and restore will apply to the group.");
        result = Regex.Replace(result, @"^Eliminar \((\d+)\)$", "Delete ($1)");
        result = Regex.Replace(result, @"^Restaurar \((\d+)\)$", "Restore ($1)");
        result = Regex.Replace(result, @"^Tema: (Claro|Oscuro|Sistema)$", match =>
            $"Theme: {match.Groups[1].Value switch { "Claro" => "Light", "Oscuro" => "Dark", _ => "System" }}");
        result = Regex.Replace(result, @"^Tamaño del texto: (Normal|Grande|Muy grande)$", match =>
            $"Text size: {match.Groups[1].Value switch { "Grande" => "Large", "Muy grande" => "Very large", _ => "Normal" }}");
        result = Regex.Replace(result, @"^(\d+) elementos?$", match =>
            $"{match.Groups[1].Value} {(match.Groups[1].Value == "1" ? "item" : "items")}");
        result = Regex.Replace(result, @"^(\d+) elementos cargados; hay más\.$", "$1 items loaded; more are available.");
        result = Regex.Replace(result, @"^(\d+) elementos\.$", "$1 items.");
        result = Regex.Replace(result, @"^1 elemento\.$", "1 item.");
        result = Regex.Replace(result, @"^Analizando el contenido de (\d+) enlaces…$", "Analyzing the content of $1 links…");
        result = Regex.Replace(result, @"^Títulos y resúmenes actualizados en (\d+) enlaces\.$", "Titles and summaries refreshed for $1 links.");
        result = Regex.Replace(result, @"^No se pudo consultar la información de (\d+) sitios\.$", "Could not retrieve information from $1 sites.");
        result = Regex.Replace(result, @"^Descarga verificada: (.+) bytes disponibles offline\.$", "Download verified: $1 bytes available offline.");
        result = Regex.Replace(result, @"^Selecciona un solo elemento para (.+)\.$", match =>
            $"Select one item to {match.Groups[1].Value switch { "editar" => "edit", "abrir" => "open", _ => match.Groups[1].Value }}.");
        result = Regex.Replace(result, @"^¿Eliminar «(.+)»\?\n\nSe moverá a la Papelera y el cambio se sincronizará\. Podrás restaurarlo después\.$",
            "Delete “$1”?\n\nIt will be moved to Trash and the change will be synced. You can restore it later.");
        result = Regex.Replace(result, @"^¿Eliminar (\d+) elementos\?\n\nSe moverán a la Papelera y los cambios se sincronizarán\. Podrás restaurarlos después\.$",
            "Delete $1 items?\n\nThey will be moved to Trash and the changes will be synced. You can restore them later.");
        result = Regex.Replace(result, @"^(\d+) elementos eliminados\. Puedes restaurarlos desde la Papelera\.$",
            "$1 items deleted. You can restore them from Trash.");
        result = Regex.Replace(result, @"^(\d+) elementos restaurados en la biblioteca\.$", "$1 items restored to the library.");
        result = Regex.Replace(result, @"^(\d+) conflicto\(s\) pendiente\(s\)\.$", "$1 pending conflicts.");
        result = Regex.Replace(result, @"^(\d+) descargas\.$", "$1 downloads.");
        result = Regex.Replace(result, @"^1 descarga\.$", "1 download.");
        return result;
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs _)
    {
        if (Choice != UiLanguageChoice.English || sender is not FrameworkElement element)
        {
            return;
        }

        if (element is Window window && !BindingOperations.IsDataBound(window, Window.TitleProperty))
        {
            window.Title = T(window.Title);
        }
        if (element is TextBlock text && !BindingOperations.IsDataBound(text, TextBlock.TextProperty))
        {
            text.Text = T(text.Text);
        }
        if (element is HeaderedItemsControl headered && headered.Header is string header &&
            !BindingOperations.IsDataBound(headered, HeaderedItemsControl.HeaderProperty))
        {
            headered.Header = T(header);
        }
        else if (element is ContentControl content && content.Content is string value &&
                 !BindingOperations.IsDataBound(content, ContentControl.ContentProperty))
        {
            content.Content = T(value);
        }
        if (element.ToolTip is string tooltip)
        {
            element.ToolTip = T(tooltip);
        }
        var name = AutomationProperties.GetName(element);
        if (!string.IsNullOrEmpty(name) && !BindingOperations.IsDataBound(element, AutomationProperties.NameProperty))
        {
            AutomationProperties.SetName(element, T(name));
        }
        var help = AutomationProperties.GetHelpText(element);
        if (!string.IsNullOrEmpty(help) && !BindingOperations.IsDataBound(element, AutomationProperties.HelpTextProperty))
        {
            AutomationProperties.SetHelpText(element, T(help));
        }
    }

    private static string ConfigurationPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UniShare", "config", "language.txt");
}
