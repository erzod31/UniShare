package org.unishare.app

import android.content.Intent
import android.content.ActivityNotFoundException
import android.net.Uri
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.core.content.FileProvider
import androidx.core.content.edit
import androidx.core.net.toUri
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.clickable
import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.text.ClickableText
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Checkbox
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.LocalTextStyle
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text as MaterialText
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalWindowInfo
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextLayoutResult
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.TextUnit
import androidx.compose.ui.unit.dp
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.LiveRegionMode
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.liveRegion
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.delay
import kotlinx.coroutines.Job
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

private enum class HomeSection(val label: String, val symbol: String) {
    LIBRARY("Biblioteca", "▣"),
    COLLECTIONS("Colecciones", "□"),
    ACTIVITY("Actividad", "◷"),
    SETTINGS("Ajustes", "⚙"),
}

@Composable
private fun Text(
    text: String,
    modifier: Modifier = Modifier,
    color: Color = Color.Unspecified,
    fontSize: TextUnit = TextUnit.Unspecified,
    fontStyle: FontStyle? = null,
    fontWeight: FontWeight? = null,
    fontFamily: FontFamily? = null,
    letterSpacing: TextUnit = TextUnit.Unspecified,
    textDecoration: TextDecoration? = null,
    textAlign: TextAlign? = null,
    lineHeight: TextUnit = TextUnit.Unspecified,
    overflow: TextOverflow = TextOverflow.Clip,
    softWrap: Boolean = true,
    maxLines: Int = Int.MAX_VALUE,
    minLines: Int = 1,
    onTextLayout: ((TextLayoutResult) -> Unit)? = null,
    style: TextStyle = LocalTextStyle.current,
) {
    MaterialText(
        text = UiStrings.translate(text), modifier = modifier, color = color, fontSize = fontSize,
        fontStyle = fontStyle, fontWeight = fontWeight, fontFamily = fontFamily,
        letterSpacing = letterSpacing, textDecoration = textDecoration, textAlign = textAlign,
        lineHeight = lineHeight, overflow = overflow, softWrap = softWrap, maxLines = maxLines,
        minLines = minLines, onTextLayout = onTextLayout, style = style,
    )
}

private data class LibraryRefreshSnapshot(
    val items: List<SavedItem>,
    val conflicts: List<AndroidSyncConflict>,
    val collections: List<Pair<String, Int>>,
    val hasMore: Boolean,
)

class MainActivity : ComponentActivity() {
    private companion object {
        const val ORIGIN_METADATA_MIGRATION = 3
        const val ORIGIN_METADATA_PREFERENCES = "origin_metadata_refresh"
    }
    private val activityScope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private lateinit var store: LocalLibraryStore
    private lateinit var offlineDownloader: OfflinePageDownloader
    private var items by mutableStateOf<List<SavedItem>>(emptyList())
    private var query by mutableStateOf("")
    private var url by mutableStateOf("")
    private var quickCollection by mutableStateOf("")
    private var saveOffline by mutableStateOf(false)
    private var captureExpanded by mutableStateOf(false)
    private var status by mutableStateOf("")
    private var syncStatus by mutableStateOf("")
    private var isSaving by mutableStateOf(false)
    private var conflicts by mutableStateOf<List<AndroidSyncConflict>>(emptyList())
    private var selectedConflict by mutableStateOf<AndroidSyncConflict?>(null)
    private var editingItem by mutableStateOf<SavedItem?>(null)
    private var pendingDeletion by mutableStateOf<SavedItem?>(null)
    private var pendingBulkDeletion by mutableStateOf(false)
    private var selectionMode by mutableStateOf(false)
    private var selectedItemIds by mutableStateOf<Set<String>>(emptySet())
    private var editingTitle by mutableStateOf("")
    private var editingSource by mutableStateOf("")
    private var editingAuthor by mutableStateOf("")
    private var editingDescription by mutableStateOf("")
    private var editingDescriptionUnlocked by mutableStateOf(false)
    private var editingTags by mutableStateOf("")
    private var editingCollections by mutableStateOf("")
    private var editingFavorite by mutableStateOf(false)
    private var favoritesOnly by mutableStateOf(false)
    private var offlineOnly by mutableStateOf(false)
    private var showArchived by mutableStateOf(false)
    private var selectedCollection by mutableStateOf<String?>(null)
    private var collectionCounts by mutableStateOf<List<Pair<String, Int>>>(emptyList())
    private var loadedItemLimit by mutableIntStateOf(200)
    private var hasMoreItems by mutableStateOf(false)
    private var refreshGeneration = 0
    private var pendingFileUrl: String? = null
    private var pendingRestoreUri by mutableStateOf<Uri?>(null)
    private var showSyncSettings by mutableStateOf(false)
    private var syncEndpoint by mutableStateOf("")
    private var syncKey by mutableStateOf("")
    private var isSyncing by mutableStateOf(false)
    private var isRefreshingOriginMetadata by mutableStateOf(false)
    private var automaticOriginRefreshStarted = false
    private var syncConfigured by mutableStateOf(false)
    private var lastSyncResult by mutableStateOf("")
    private var pairingJustCompleted = false
    private var pollingJob: Job? = null
    private var themePreference by mutableStateOf(ThemePreference.SYSTEM)
    private var uiLanguage by mutableStateOf(UiLanguageChoice.SPANISH)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val uniShare = application as UniShareApplication
        store = uniShare.store
        offlineDownloader = uniShare.offlineDownloader
        themePreference = ThemeSettings.load(this)
        uiLanguage = UiLanguageSettings.load(this)
        UiStrings.current = uiLanguage
        if (savedInstanceState == null) {
            val pairingIntent = handlePairingIntent(intent)
            if (!pairingIntent) {
                receiveShare(intent)
            }
            if (pairingIntent || intent?.action == Intent.ACTION_SEND) {
                setIntent(Intent(this, MainActivity::class.java))
            }
        }
        DirectSyncPreferences.load(this)?.let {
            syncEndpoint = it.endpoint
            syncKey = it.pairingKey
            syncConfigured = true
        }
        lastSyncResult = DirectSyncPreferences.lastResult(this).orEmpty()
        setContent { UniShareTheme(themePreference) { LibraryScreen() } }
        activityScope.launch(Dispatchers.IO) { store.collectOrphanedBlobs() }
        DirectSyncScheduler.schedule(this)
        refresh()
    }

    override fun onStart() {
        super.onStart()
        synchronizeDirect(showSuccess = pairingJustCompleted)
        pairingJustCompleted = false
        if (!automaticOriginRefreshStarted) {
            automaticOriginRefreshStarted = true
            activityScope.launch {
                while (isSyncing) delay(250)
                if (originMetadataMigrationPending()) refreshOriginMetadata(automaticOnly = true)
            }
        }
        pollingJob?.cancel()
        pollingJob = activityScope.launch {
            while (isActive) {
                delay(15_000)
                pollDirectChanges()
            }
        }
    }

    override fun onStop() {
        pollingJob?.cancel()
        pollingJob = null
        super.onStop()
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        if (handlePairingIntent(intent)) {
            setIntent(Intent(this, MainActivity::class.java))
            synchronizeDirect(showSuccess = true)
        } else {
            receiveShare(intent)
            setIntent(Intent(this, MainActivity::class.java))
        }
    }

    override fun onDestroy() {
        activityScope.cancel()
        super.onDestroy()
    }

    private fun receiveShare(intent: Intent?) {
        if (intent?.action != Intent.ACTION_SEND) return
        val shared = SharePayload.parse(intent.getStringExtra(Intent.EXTRA_TEXT))
        @Suppress("DEPRECATION")
        val stream = intent.getParcelableExtra<Uri>(Intent.EXTRA_STREAM)
        if (stream != null) {
            importFile(stream, shared?.url, "")
            return
        }
        if (shared == null) {
            status = "El contenido compartido no es una URL http/https válida."
            return
        }
        activityScope.launch {
            try {
                val item = withContext(Dispatchers.IO) { store.saveLink(shared.title, shared.url) }
                DirectSyncPreferences.markLocalChangePending(this@MainActivity)
                runCatching {
                    withContext(Dispatchers.IO) {
                        offlineDownloader.fetchMetadata(shared.url)?.let { store.applyFetchedMetadata(item.id, it) }
                    }
                }
                status = "Enlace compartido guardado en este dispositivo."
                query = ""
                url = ""
                refresh()
                syncAfterChange()
            } catch (error: Exception) {
                status = error.message ?: "No se pudo guardar el enlace compartido."
            }
        }
    }

    private fun handlePairingIntent(intent: Intent?): Boolean {
        if (intent?.action != Intent.ACTION_VIEW || intent.data?.scheme != "unishare") return false
        return try {
            val settings = DirectSyncPairing.parse(intent.data.toString())
            DirectSyncPreferences.save(this, settings.endpoint, settings.pairingKey)
            syncEndpoint = settings.endpoint
            syncKey = settings.pairingKey
            syncConfigured = true
            pairingJustCompleted = true
            status = "Teléfono conectado. Descargando tu biblioteca…"
            DirectSyncScheduler.schedule(this)
            true
        } catch (error: Exception) {
            status = error.message ?: "No se pudo leer el QR de emparejamiento."
            true
        }
    }

    private fun importFile(uri: Uri, sourceUrl: String? = null, collections: String = quickCollection) {
        activityScope.launch {
            try {
                val item = withContext(Dispatchers.IO) { store.importFile(uri, sourceUrl, collections) }
                DirectSyncPreferences.markLocalChangePending(this@MainActivity)
                status = if (item.kind == 3) {
                    "Elemento híbrido guardado localmente."
                } else {
                    "Archivo guardado localmente."
                }
                query = ""
                url = ""
                refresh()
                syncAfterChange()
            } catch (error: Exception) {
                status = error.message ?: "No se pudo importar el archivo."
            }
        }
    }

    private fun save() {
        if (isSaving) return
        isSaving = true
        activityScope.launch {
            try {
                val item = withContext(Dispatchers.IO) { store.saveLink("", url, quickCollection) }
                DirectSyncPreferences.markLocalChangePending(this@MainActivity)
                url = ""
                var downloadError: Exception? = null
                var metadataError: Exception? = null
                if (saveOffline) {
                    status = "Enlace guardado. Descargando la copia offline…"
                    try {
                        withContext(Dispatchers.IO) { downloadOffline(item) }
                    } catch (error: Exception) {
                        downloadError = error
                    }
                } else {
                    try {
                        withContext(Dispatchers.IO) {
                            offlineDownloader.fetchMetadata(item.originalUrl!!)?.let {
                                store.applyFetchedMetadata(item.id, it)
                            }
                        }
                    } catch (error: Exception) {
                        metadataError = error
                    }
                }
                status = when {
                    downloadError != null ->
                        "Enlace guardado; la copia offline falló y puede reintentarse: ${downloadError.message}"
                    saveOffline -> "Enlace y copia offline guardados en el teléfono."
                    metadataError != null -> "Enlace guardado con título provisional; no se pudieron consultar sus metadatos."
                    else -> "Enlace guardado localmente."
                }
                captureExpanded = false
                refresh()
                syncAfterChange()
            } catch (error: Exception) {
                status = error.message ?: "No se pudo guardar el enlace."
            } finally {
                isSaving = false
            }
        }
    }

    private fun refreshOriginMetadata(automaticOnly: Boolean) {
        if (isRefreshingOriginMetadata) {
            if (!automaticOnly) status = "Ya se están actualizando los títulos y los resúmenes."
            return
        }
        isRefreshingOriginMetadata = true
        activityScope.launch {
            try {
                val candidates = withContext(Dispatchers.IO) {
                    store.listItemsForMetadataRefresh()
                }
                if (!automaticOnly) {
                    status = if (candidates.isEmpty()) {
                        "No hay enlaces que actualizar."
                    } else {
                        "Analizando el contenido de ${candidates.size} enlaces…"
                    }
                }

                var updated = 0
                var unavailable = 0
                for (item in candidates) {
                    try {
                        val changed = withContext(Dispatchers.IO) {
                            val metadata = offlineDownloader.fetchMetadata(item.originalUrl!!) ?: return@withContext false
                            store.applyFetchedMetadata(item.id, metadata)
                        }
                        if (changed) {
                            DirectSyncPreferences.markLocalChangePending(this@MainActivity)
                            updated++
                        }
                    } catch (_: Exception) {
                        unavailable++
                    }
                }
                if (updated > 0) {
                    refresh()
                    status = "Títulos y resúmenes actualizados en $updated enlaces." +
                        if (unavailable > 0) " $unavailable sitios no respondieron." else ""
                    syncAfterChange()
                } else if (!automaticOnly && candidates.isNotEmpty()) {
                    status = if (unavailable > 0) {
                        "No se pudo consultar la información de $unavailable sitios."
                    } else {
                        "Los títulos y los resúmenes ya estaban actualizados."
                    }
                }
                if (automaticOnly && (candidates.isEmpty() || unavailable < candidates.size)) {
                    markOriginMetadataMigrationCompleted()
                }
            } finally {
                isRefreshingOriginMetadata = false
            }
        }
    }

    private fun originMetadataMigrationPending(): Boolean =
        getSharedPreferences(ORIGIN_METADATA_PREFERENCES, MODE_PRIVATE)
            .getInt("completed_version", 0) < ORIGIN_METADATA_MIGRATION

    private fun markOriginMetadataMigrationCompleted() {
        getSharedPreferences(ORIGIN_METADATA_PREFERENCES, MODE_PRIVATE).edit {
            putInt("completed_version", ORIGIN_METADATA_MIGRATION)
        }
    }

    private fun retryOffline(item: SavedItem) {
        if (item.originalUrl == null) return
        activityScope.launch {
            try {
                status = "Descargando la copia offline…"
                withContext(Dispatchers.IO) { downloadOffline(item) }
                DirectSyncPreferences.markLocalChangePending(this@MainActivity)
                status = "Copia offline guardada en el teléfono."
                editingItem = null
                refresh()
                syncAfterChange()
            } catch (error: Exception) {
                editingItem = null
                status = error.message ?: "No se pudo guardar la copia offline."
                refresh()
            }
        }
    }

    private fun downloadOffline(item: SavedItem) {
        val sourceUrl = requireNotNull(item.originalUrl) { "El elemento no tiene un enlace descargable" }
        val downloaded = offlineDownloader.download(sourceUrl)
        try {
            store.attachDownloadedAsset(item.id, downloaded)
        } finally {
            downloaded.file.delete()
        }
    }

    private fun exportBackup(uri: Uri) {
        activityScope.launch {
            try {
                val summary = withContext(Dispatchers.IO) { store.exportBackup(uri) }
                status = "Paquete listo: ${summary.itemCount} elementos y ${summary.blobCount} archivos."
            } catch (error: Exception) {
                status = error.message ?: "No se pudo exportar el respaldo."
            }
        }
    }

    private fun restoreBackup(uri: Uri) {
        activityScope.launch {
            try {
                val summary = withContext(Dispatchers.IO) { store.restoreBackup(uri) }
                if (summary.importedItems > 0 || summary.updatedItems > 0) {
                    DirectSyncPreferences.markLocalChangePending(this@MainActivity)
                }
                status = "Copia incorporada: ${summary.importedItems} nuevos, " +
                    "${summary.updatedItems} actualizados y ${summary.conflicts} conflictos."
                refresh()
                syncAfterChange()
            } catch (error: Exception) {
                status = error.message ?: "No se pudo restaurar la copia."
            }
        }
    }

    private fun resolveConflict(conflict: AndroidSyncConflict, useRemote: Boolean) {
        activityScope.launch {
            try {
                withContext(Dispatchers.IO) { store.resolveSyncConflict(conflict.id, useRemote) }
                DirectSyncPreferences.markLocalChangePending(this@MainActivity)
                selectedConflict = null
                status = if (useRemote) "Se aplicó la versión recibida." else "Se conservó tu versión."
                refresh()
                syncAfterChange()
            } catch (error: Exception) {
                status = error.message ?: "No se pudo resolver el conflicto."
            }
        }
    }

    private fun beginEditing(item: SavedItem) {
        editingItem = item
        editingTitle = item.title
        editingSource = item.source.orEmpty()
        editingAuthor = item.author.orEmpty()
        editingDescription = item.description.orEmpty()
        editingDescriptionUnlocked = false
        editingTags = item.tags.joinToString(", ")
        editingCollections = item.collections.joinToString(", ")
        editingFavorite = item.favorite
    }

    private fun saveEditedItem() {
        val item = editingItem ?: return
        activityScope.launch {
            try {
                withContext(Dispatchers.IO) {
                    store.updateItem(
                        item.id,
                        editingTitle,
                        editingSource,
                        editingAuthor,
                        editingDescription,
                        editingFavorite,
                        editingTags,
                        editingCollections,
                    )
                }
                DirectSyncPreferences.markLocalChangePending(this@MainActivity)
                editingItem = null
                status = "Cambios guardados."
                refresh()
                syncAfterChange()
            } catch (error: Exception) {
                status = error.message ?: "No se pudieron guardar los cambios."
            }
        }
    }

    private fun setArchived(item: SavedItem, archived: Boolean) {
        activityScope.launch {
            try {
                withContext(Dispatchers.IO) { store.setArchived(item.id, archived) }
                DirectSyncPreferences.markLocalChangePending(this@MainActivity)
                editingItem = null
                status = if (archived) {
                    "Elemento eliminado. Puedes restaurarlo desde Ajustes > Mostrar papelera."
                } else {
                    "Elemento restaurado en la biblioteca."
                }
                refresh()
                syncAfterChange()
            } catch (error: Exception) {
                status = error.message ?: "No se pudo actualizar el elemento."
            }
        }
    }

    private fun setSelectedArchived(archived: Boolean) {
        val selected = ItemBatchSelection.eligibleIds(items, selectedItemIds, archived)
        if (selected.isEmpty()) {
            clearSelection()
            return
        }
        activityScope.launch {
            try {
                withContext(Dispatchers.IO) { store.setArchived(selected, archived) }
                DirectSyncPreferences.markLocalChangePending(this@MainActivity)
                clearSelection()
                status = if (archived) {
                    "${selected.size} elementos eliminados. Puedes restaurarlos desde Ajustes > Mostrar papelera."
                } else {
                    "${selected.size} elementos restaurados en la biblioteca."
                }
                refresh()
                syncAfterChange()
            } catch (error: Exception) {
                status = error.message ?: "No se pudieron actualizar los elementos."
            }
        }
    }

    private fun toggleSelection(itemId: String) {
        selectedItemIds = ItemBatchSelection.toggle(selectedItemIds, itemId)
        selectionMode = selectedItemIds.isNotEmpty()
    }

    private fun clearSelection() {
        selectedItemIds = emptySet()
        selectionMode = false
    }

    private fun openItem(item: SavedItem) {
        activityScope.launch {
            try {
                val asset = withContext(Dispatchers.IO) { store.localAsset(item.id) }
                val intent = if (asset != null) {
                    Intent(Intent.ACTION_VIEW).apply {
                        val contentUri = FileProvider.getUriForFile(
                            this@MainActivity,
                            "$packageName.files",
                            asset.file,
                        )
                        setDataAndType(contentUri, asset.mimeType)
                        addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                    }
                } else {
                    Intent(Intent.ACTION_VIEW, requireNotNull(item.originalUrl) {
                        "Este elemento no tiene contenido disponible"
                    }.toUri())
                }
                startActivity(intent)
            } catch (_: ActivityNotFoundException) {
                status = "No hay una aplicación instalada que pueda abrir este contenido."
            } catch (error: Exception) {
                status = error.message ?: "No se pudo abrir el elemento."
            }
        }
    }

    private fun openWebLink(url: String) {
        try {
            startActivity(Intent(Intent.ACTION_VIEW, url.toUri()))
        } catch (_: ActivityNotFoundException) {
            status = "No hay una aplicación instalada que pueda abrir este enlace."
        } catch (error: Exception) {
            status = error.message ?: "No se pudo abrir el enlace."
        }
    }

    private fun refresh() {
        val generation = ++refreshGeneration
        val requestedQuery = query
        val requestedFavoritesOnly = favoritesOnly
        val requestedArchived = showArchived
        val requestedCollection = selectedCollection
        val requestedOfflineOnly = offlineOnly
        val requestedLimit = loadedItemLimit
        activityScope.launch {
            val snapshot = withContext(Dispatchers.IO) {
                val listedItems = store.listItems(
                    requestedQuery,
                    requestedFavoritesOnly,
                    requestedArchived,
                    requestedCollection,
                    requestedOfflineOnly,
                    requestedLimit + 1,
                )
                val visibleItems = listedItems.take(requestedLimit)
                LibraryRefreshSnapshot(
                    visibleItems,
                    store.listSyncConflicts(),
                    store.listCollections(),
                    listedItems.size > requestedLimit,
                )
            }
            if (generation != refreshGeneration) return@launch
            items = snapshot.items
            val retainedSelection = ItemBatchSelection.retainVisible(selectedItemIds, snapshot.items)
            if (selectedItemIds.isNotEmpty() && retainedSelection.isEmpty()) selectionMode = false
            selectedItemIds = retainedSelection
            conflicts = snapshot.conflicts
            collectionCounts = snapshot.collections
            hasMoreItems = snapshot.hasMore
        }
    }

    private fun syncAfterChange() {
        synchronizeDirect(showSuccess = false)
    }

    private fun synchronizeDirect(showSuccess: Boolean) {
        val settings = DirectSyncPreferences.load(this) ?: return
        if (isSyncing) return
        isSyncing = true
        activityScope.launch {
            try {
                val result = withContext(Dispatchers.IO) {
                    DirectSyncCoordinator.synchronize(this@MainActivity, store, settings)
                }
                lastSyncResult = DirectSyncPreferences.lastResult(this@MainActivity).orEmpty()
                refresh()
                if (showSuccess || result.receivedItems > 0 || result.updatedItems > 0 || result.conflicts > 0) {
                    syncStatus = "Sincronizado: ${result.receivedItems} nuevos, ${result.updatedItems} actualizados" +
                        if (result.conflicts > 0) " y ${result.conflicts} conflictos." else "."
                } else {
                    syncStatus = ""
                }
            } catch (error: Exception) {
                DirectSyncScheduler.enqueueNow(this@MainActivity)
                lastSyncResult = DirectSyncPreferences.lastResult(this@MainActivity).orEmpty()
                syncStatus = "Sincronización pendiente: " +
                    (error.message ?: "ordenador no disponible")
            } finally {
                isSyncing = false
            }
        }
    }

    private fun pollDirectChanges() {
        val settings = DirectSyncPreferences.load(this) ?: return
        if (isSyncing) return
        activityScope.launch {
            try {
                val revision = withContext(Dispatchers.IO) { store.directServerRevision(settings) }
                if (revision != DirectSyncPreferences.lastRevision(this@MainActivity) ||
                    DirectSyncPreferences.localChangesPending(this@MainActivity) ||
                    withContext(Dispatchers.IO) { store.hasPendingDirectOperations() }
                ) {
                    synchronizeDirect(showSuccess = false)
                } else {
                    DirectSyncCoordinator.markVerifiedUpToDate(this@MainActivity)
                    lastSyncResult = DirectSyncPreferences.lastResult(this@MainActivity).orEmpty()
                    syncStatus = ""
                }
            } catch (error: Exception) {
                val message = DirectSyncErrorMessages.forUser(error)
                DirectSyncPreferences.writeLastResult(this@MainActivity, "pendiente: $message")
                lastSyncResult = DirectSyncPreferences.lastResult(this@MainActivity).orEmpty()
                syncStatus = "Sincronización pendiente: $message"
            }
        }
    }

    private fun saveSyncSettings() {
        try {
            DirectSyncPreferences.save(this, syncEndpoint, syncKey)
            syncConfigured = true
            DirectSyncScheduler.schedule(this)
            showSyncSettings = false
            status = "Configuración guardada. Conectando con el ordenador…"
            synchronizeDirect(showSuccess = true)
        } catch (error: Exception) {
            status = error.message ?: "Configuración de sincronización no válida."
        }
    }

    private fun openTailscale() {
        val launchIntent = packageManager.getLaunchIntentForPackage("com.tailscale.ipn")
        if (launchIntent == null) {
            status = "Abre Tailscale manualmente y comprueba que la conexión y su DNS estén activados."
        } else {
            startActivity(launchIntent)
        }
    }

    @Suppress("DEPRECATION")
    @Composable
    private fun ClickableSummaryText(
        text: String,
        modifier: Modifier = Modifier,
        maxLines: Int = Int.MAX_VALUE,
    ) {
        val links = remember(text) { SummaryLinkParser.find(text) }
        if (links.isEmpty()) {
            MaterialText(
                text,
                modifier = modifier,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = maxLines,
            )
            return
        }

        val linkColor = MaterialTheme.colorScheme.primary
        val annotatedText = remember(text, links, linkColor) {
            buildAnnotatedString {
                append(text)
                links.forEach { link ->
                    addStyle(
                        SpanStyle(color = linkColor, textDecoration = TextDecoration.Underline),
                        link.start,
                        link.endExclusive,
                    )
                    addStringAnnotation("URL", link.url, link.start, link.endExclusive)
                }
            }
        }
        ClickableText(
            text = annotatedText,
            modifier = modifier,
            style = MaterialTheme.typography.bodySmall.copy(
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            ),
            maxLines = maxLines,
        ) { offset ->
            annotatedText.getStringAnnotations("URL", offset, offset)
                .firstOrNull()
                ?.let { openWebLink(it.item) }
        }
    }

    @Composable
    private fun LibraryItemCard(item: SavedItem) {
        val selected = item.id in selectedItemIds
        val symbol = when (item.kind) {
            2 -> "▤"
            3 -> "◆"
            else -> "↗"
        }
        val subtitle = when {
            item.originalUrl != null && item.assetSha256 != null -> "${item.originalUrl} · Disponible offline"
            item.originalUrl != null -> item.originalUrl
            item.assetSha256 != null -> "Archivo local disponible"
            else -> "Sin contenido local"
        }
        Card(
            modifier = Modifier
                .fillMaxWidth()
                .clickable(
                    role = Role.Button,
                    onClickLabel = UiStrings.translate(if (selectionMode) "Cambiar selección" else "Abrir opciones del elemento"),
                ) {
                    if (selectionMode) toggleSelection(item.id) else beginEditing(item)
                }
                .semantics {
                    contentDescription = UiStrings.translate(buildString {
                        append(item.title)
                        append(". ")
                        append(
                            when (item.kind) {
                                2 -> "Archivo"
                                3 -> "Enlace y archivo"
                                else -> "Enlace"
                            },
                        )
                        if (item.favorite) append(". Favorito")
                        if (item.assetSha256 != null) append(". Disponible sin conexión")
                        if (item.deletedAt != null) append(". En la papelera")
                        if (selected) append(". Seleccionado")
                    })
                },
            shape = RoundedCornerShape(14.dp),
            colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceContainerLow),
        ) {
            Row(
                modifier = Modifier.fillMaxWidth().padding(13.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Surface(
                    modifier = Modifier.size(44.dp),
                    shape = RoundedCornerShape(12.dp),
                    color = MaterialTheme.colorScheme.primaryContainer,
                ) {
                    Box(contentAlignment = Alignment.Center) {
                        Text(
                            symbol,
                            modifier = Modifier.clearAndSetSemantics { },
                            style = MaterialTheme.typography.titleLarge,
                            color = MaterialTheme.colorScheme.primary,
                        )
                    }
                }
                Column(modifier = Modifier.weight(1f).padding(horizontal = 12.dp)) {
                    MaterialText(
                        buildString {
                            if (item.favorite) append("★ ")
                            append(item.title)
                            if (item.deletedAt != null) append(" · En la papelera")
                        },
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.SemiBold,
                    )
                    MaterialText(
                        subtitle,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        maxLines = 1,
                    )
                    if (!item.description.isNullOrBlank()) {
                        ClickableSummaryText(
                            text = item.description,
                            modifier = Modifier.padding(top = 2.dp),
                            maxLines = 2,
                        )
                    }
                    val folder = item.collections.firstOrNull()
                    if (folder != null) {
                        MaterialText(
                            "□ $folder",
                            modifier = Modifier.padding(top = 4.dp),
                            style = MaterialTheme.typography.labelMedium,
                            color = MaterialTheme.colorScheme.primary,
                        )
                    }
                }
                Column(horizontalAlignment = Alignment.End) {
                    if (selectionMode) {
                        Checkbox(
                            checked = selected,
                            onCheckedChange = { toggleSelection(item.id) },
                            modifier = Modifier.semantics {
                                contentDescription = if (UiStrings.current == UiLanguageChoice.ENGLISH)
                                    "Select ${item.title}" else "Seleccionar ${item.title}"
                            },
                        )
                    }
                    if (item.assetSha256 != null) {
                        Text("↓", color = MaterialTheme.colorScheme.tertiary, style = MaterialTheme.typography.titleMedium)
                    }
                    Text("›", style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onSurfaceVariant)
                }
            }
        }
    }

    @OptIn(ExperimentalMaterial3Api::class)
    @Composable
    private fun LibraryScreen() {
        UiStrings.current = uiLanguage
        var optionsExpanded by remember { mutableStateOf(false) }
        var section by remember { mutableStateOf(HomeSection.LIBRARY) }
        val configuration = LocalConfiguration.current
        val windowInfo = LocalWindowInfo.current
        val density = LocalDensity.current
        val windowWidth = with(density) { windowInfo.containerSize.width.toDp() }
        val compactLayout = windowWidth < 380.dp || configuration.fontScale > 1.3f
        val filePicker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
            val sourceUrl = pendingFileUrl
            pendingFileUrl = null
            if (uri != null) importFile(uri, sourceUrl)
        }
        val backupCreator = rememberLauncherForActivityResult(
            ActivityResultContracts.CreateDocument("application/zip"),
        ) { uri ->
            if (uri != null) exportBackup(uri)
        }
        val backupPicker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
            if (uri != null) pendingRestoreUri = uri
        }
        val chooseFile = {
            val associatedUrl = url.trim()
            if (associatedUrl.isNotEmpty() && !SharePayload.isWebUrl(associatedUrl)) {
                status = "Usa un enlace http o https válido para asociarlo al archivo."
            } else {
                pendingFileUrl = associatedUrl.ifEmpty { null }
                filePicker.launch(arrayOf("*/*"))
            }
        }

        val displayedItems = if (offlineOnly) {
            items.filter { it.assetSha256 != null }
        } else {
            items
        }

        Scaffold(
            topBar = {
                TopAppBar(
                    title = {
                        if (compactLayout) {
                            Text(section.label, fontWeight = FontWeight.SemiBold)
                        } else {
                            Column {
                                Text("UniShare", style = MaterialTheme.typography.labelMedium, color = MaterialTheme.colorScheme.primary)
                                Text(section.label, fontWeight = FontWeight.SemiBold)
                            }
                        }
                    },
                    actions = {
                        if (syncConfigured) {
                            val synchronizationSucceeded =
                                !DirectSyncPreferences.localChangesPending(this@MainActivity) &&
                                    lastSyncResult.substringAfter(": ", "").startsWith("correcto")
                            val synchronizationLabel = when {
                                isSyncing -> "Sincronizando"
                                synchronizationSucceeded -> "Sincronizado"
                                else -> "Sincronización pendiente"
                            }
                            Text(
                                if (compactLayout) "●" else "● $synchronizationLabel",
                                modifier = Modifier.semantics {
                                    contentDescription = synchronizationLabel
                                },
                                style = MaterialTheme.typography.labelMedium,
                                color = if (synchronizationSucceeded) {
                                    MaterialTheme.colorScheme.tertiary
                                } else {
                                    MaterialTheme.colorScheme.error
                                },
                            )
                        }
                        Box {
                            TextButton(
                                onClick = { optionsExpanded = true },
                                modifier = Modifier.semantics { contentDescription = UiStrings.translate("Más opciones") },
                            ) { Text("⋮", style = MaterialTheme.typography.titleLarge) }
                            DropdownMenu(
                                expanded = optionsExpanded,
                                onDismissRequest = { optionsExpanded = false },
                                containerColor = MaterialTheme.colorScheme.surfaceContainer,
                            ) {
                                DropdownMenuItem(
                                    text = { Text("Exportar copia de seguridad…", color = MaterialTheme.colorScheme.onSurface) },
                                    onClick = { optionsExpanded = false; backupCreator.launch("unishare-respaldo.unishare.zip") },
                                )
                                DropdownMenuItem(
                                    text = { Text("Restaurar copia sin borrar datos…", color = MaterialTheme.colorScheme.onSurface) },
                                    onClick = {
                                        optionsExpanded = false
                                        backupPicker.launch(arrayOf("application/zip", "application/octet-stream"))
                                    },
                                )
                                DropdownMenuItem(
                                    text = { Text("Conectar con Windows…", color = MaterialTheme.colorScheme.onSurface) },
                                    onClick = { optionsExpanded = false; showSyncSettings = true },
                                )
                                DropdownMenuItem(
                                    text = { Text("Resolver conflictos (${conflicts.size})", color = MaterialTheme.colorScheme.onSurface) },
                                    enabled = conflicts.isNotEmpty(),
                                    onClick = { optionsExpanded = false; selectedConflict = conflicts.firstOrNull() },
                                )
                                DropdownMenuItem(
                                    text = { Text("Tema: ${themePreference.label}", color = MaterialTheme.colorScheme.onSurface) },
                                    onClick = {
                                        themePreference = themePreference.next()
                                        ThemeSettings.save(this@MainActivity, themePreference)
                                        optionsExpanded = false
                                    },
                                )
                            }
                        }
                    },
                    colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.background),
                )
            },
            bottomBar = {
                NavigationBar(containerColor = MaterialTheme.colorScheme.surface) {
                    HomeSection.entries.forEach { destination ->
                        NavigationBarItem(
                            selected = section == destination,
                            onClick = { clearSelection(); section = destination },
                            modifier = Modifier.semantics { contentDescription = UiStrings.translate(destination.label) },
                            icon = {
                                Text(
                                    destination.symbol,
                                    modifier = Modifier.clearAndSetSemantics { },
                                    style = MaterialTheme.typography.titleMedium,
                                )
                            },
                            label = if (compactLayout) null else {
                                { Text(destination.label, maxLines = 1) }
                            },
                        )
                    }
                }
            },
            floatingActionButton = {
                if (section == HomeSection.LIBRARY && !selectionMode) {
                    FloatingActionButton(
                        onClick = { captureExpanded = true },
                        modifier = Modifier.semantics { contentDescription = UiStrings.translate("Guardar un elemento") },
                    ) {
                        Text("＋", style = MaterialTheme.typography.headlineSmall)
                    }
                }
            },
        ) { padding ->
            Box(
                modifier = Modifier.fillMaxSize().padding(padding),
                contentAlignment = Alignment.TopCenter,
            ) {
                Column(
                    modifier = Modifier.fillMaxSize().widthIn(max = 840.dp).padding(horizontal = 16.dp),
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                if (!syncConfigured) {
                    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.errorContainer)) {
                        Column(modifier = Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(7.dp)) {
                            Text("Conecta este teléfono", fontWeight = FontWeight.SemiBold)
                            Text(
                                "En Windows abre Opciones y herramientas > Conectar teléfono y escanea el QR.",
                                style = MaterialTheme.typography.bodySmall,
                            )
                            OutlinedButton(onClick = { showSyncSettings = true }) { Text("Conectar manualmente") }
                        }
                    }
                }

                when (section) {
                    HomeSection.LIBRARY -> {
                        OutlinedTextField(
                            value = query,
                            onValueChange = { query = it; loadedItemLimit = 200; refresh() },
                            label = { Text("Buscar en tu biblioteca") },
                            placeholder = { Text("Título, URL, notas o etiquetas") },
                            singleLine = true,
                            modifier = Modifier.fillMaxWidth(),
                            shape = RoundedCornerShape(14.dp),
                        )
                        Row(
                            modifier = Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()),
                            horizontalArrangement = Arrangement.spacedBy(8.dp),
                        ) {
                            FilterChip(
                                selected = !favoritesOnly && !offlineOnly && selectedCollection == null,
                                onClick = { favoritesOnly = false; offlineOnly = false; selectedCollection = null; loadedItemLimit = 200; refresh() },
                                label = { Text("Todos") },
                            )
                            FilterChip(
                                selected = favoritesOnly,
                                onClick = { favoritesOnly = !favoritesOnly; offlineOnly = false; selectedCollection = null; loadedItemLimit = 200; refresh() },
                                label = { Text("Favoritos") },
                            )
                            FilterChip(
                                selected = offlineOnly,
                                onClick = {
                                    offlineOnly = !offlineOnly
                                    favoritesOnly = false
                                    selectedCollection = null
                                    loadedItemLimit = 200
                                    refresh()
                                },
                                label = { Text("Offline") },
                            )
                        }
                        selectedCollection?.let { collection ->
                            FilterChip(
                                selected = true,
                                onClick = { selectedCollection = null; loadedItemLimit = 200; refresh() },
                                label = { Text("Carpeta: $collection  ×") },
                            )
                        }
                        if (status.isNotBlank()) {
                            Surface(
                                modifier = Modifier.semantics { liveRegion = LiveRegionMode.Polite },
                                color = MaterialTheme.colorScheme.secondaryContainer,
                                shape = RoundedCornerShape(10.dp),
                            ) {
                                Text(
                                    status,
                                    modifier = Modifier.padding(10.dp),
                                    style = MaterialTheme.typography.bodySmall,
                                )
                            }
                        }
                        if (syncStatus.isNotBlank()) {
                            Surface(
                                modifier = Modifier.semantics { liveRegion = LiveRegionMode.Polite },
                                color = MaterialTheme.colorScheme.secondaryContainer,
                                shape = RoundedCornerShape(10.dp),
                            ) {
                                Column(
                                    modifier = Modifier.padding(10.dp),
                                    verticalArrangement = Arrangement.spacedBy(7.dp),
                                ) {
                                    Text(syncStatus, style = MaterialTheme.typography.bodySmall)
                                    if (syncStatus.startsWith("Sincronización pendiente:")) {
                                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                            TextButton(onClick = { synchronizeDirect(showSuccess = true) }) {
                                                Text("Reintentar")
                                            }
                                            TextButton(onClick = ::openTailscale) { Text("Abrir Tailscale") }
                                        }
                                    }
                                }
                            }
                        }
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Text("Tus elementos", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                            Row(verticalAlignment = Alignment.CenterVertically) {
                                Text("${displayedItems.size}", color = MaterialTheme.colorScheme.onSurfaceVariant)
                                if (displayedItems.isNotEmpty()) {
                                    TextButton(
                                        onClick = {
                                            if (selectionMode) clearSelection() else selectionMode = true
                                        },
                                    ) { Text(if (selectionMode) "Cancelar" else "Seleccionar") }
                                }
                            }
                        }
                        if (selectionMode) {
                            val selectedVisible = displayedItems.filter { it.id in selectedItemIds }
                            val selectedActive = selectedVisible.count { it.deletedAt == null }
                            val selectedArchived = selectedVisible.size - selectedActive
                            Surface(
                                modifier = Modifier.fillMaxWidth().semantics {
                                    liveRegion = LiveRegionMode.Polite
                                    contentDescription = UiStrings.translate("${selectedVisible.size} elementos seleccionados")
                                },
                                color = MaterialTheme.colorScheme.secondaryContainer,
                                shape = RoundedCornerShape(12.dp),
                            ) {
                                Column(
                                    modifier = Modifier.padding(10.dp),
                                    verticalArrangement = Arrangement.spacedBy(6.dp),
                                ) {
                                    Text(
                                        "${selectedVisible.size} seleccionados",
                                        fontWeight = FontWeight.SemiBold,
                                    )
                                    Row(
                                        modifier = Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()),
                                        horizontalArrangement = Arrangement.spacedBy(6.dp),
                                    ) {
                                        TextButton(
                                            onClick = {
                                                selectedItemIds = displayedItems.mapTo(mutableSetOf()) { it.id }
                                            },
                                        ) { Text("Todos") }
                                        if (selectedActive > 0) {
                                            TextButton(
                                                onClick = { pendingBulkDeletion = true },
                                                colors = ButtonDefaults.textButtonColors(
                                                    contentColor = MaterialTheme.colorScheme.error,
                                                ),
                                            ) { Text("Eliminar ($selectedActive)") }
                                        }
                                        if (selectedArchived > 0) {
                                            TextButton(onClick = { setSelectedArchived(false) }) {
                                                Text("Restaurar ($selectedArchived)")
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        if (displayedItems.isEmpty()) {
                            val emptyTitle = when {
                                query.isNotBlank() -> "No hay resultados"
                                favoritesOnly -> "No tienes favoritos"
                                offlineOnly -> "No hay copias offline"
                                showArchived -> "La papelera está vacía"
                                else -> "No hay elementos para mostrar"
                            }
                            val emptyDescription = when {
                                query.isNotBlank() -> "Prueba con otros términos o borra la búsqueda."
                                favoritesOnly -> "Marca un elemento como favorito desde Editar información."
                                offlineOnly -> "Guarda una copia offline desde las opciones de un enlace."
                                showArchived -> "Los elementos que elimines aparecerán aquí y podrás restaurarlos."
                                syncConfigured -> "Guarda un enlace o cambia los filtros."
                                else -> "Conecta primero este teléfono para recuperar tu biblioteca."
                            }
                            Column(
                                modifier = Modifier.fillMaxWidth().weight(1f),
                                horizontalAlignment = Alignment.CenterHorizontally,
                                verticalArrangement = Arrangement.Center,
                            ) {
                                Text("＋", style = MaterialTheme.typography.displaySmall, color = MaterialTheme.colorScheme.primary)
                                Text(emptyTitle, fontWeight = FontWeight.SemiBold)
                                Text(
                                    emptyDescription,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                            }
                        } else {
                            LazyColumn(
                                modifier = Modifier.weight(1f),
                                verticalArrangement = Arrangement.spacedBy(9.dp),
                            ) {
                                items(displayedItems, key = { it.id }) { item -> LibraryItemCard(item) }
                                if (hasMoreItems) {
                                    item {
                                        OutlinedButton(
                                            onClick = { loadedItemLimit += 200; refresh() },
                                            modifier = Modifier.fillMaxWidth(),
                                        ) { Text("Cargar más") }
                                    }
                                }
                                item { Spacer(Modifier.height(80.dp)) }
                            }
                        }
                    }
                    HomeSection.COLLECTIONS -> {
                        Text("Organiza sin mover archivos físicos", color = MaterialTheme.colorScheme.onSurfaceVariant)
                        if (collectionCounts.isEmpty()) {
                            Column(
                                modifier = Modifier.fillMaxWidth().weight(1f),
                                horizontalAlignment = Alignment.CenterHorizontally,
                                verticalArrangement = Arrangement.Center,
                            ) {
                                Text("□", style = MaterialTheme.typography.displaySmall, color = MaterialTheme.colorScheme.primary)
                                Text("Aún no hay carpetas", fontWeight = FontWeight.SemiBold)
                                Text("Puedes crear una al guardar o editar un elemento.")
                            }
                        } else {
                            LazyColumn(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                                items(collectionCounts, key = { it.first }) { (name, count) ->
                                    Card(
                                        modifier = Modifier.fillMaxWidth().clickable(role = Role.Button) {
                                            selectedCollection = name
                                            query = ""
                                            favoritesOnly = false
                                            offlineOnly = false
                                            loadedItemLimit = 200
                                            section = HomeSection.LIBRARY
                                            refresh()
                                        },
                                    ) {
                                        Row(
                                            modifier = Modifier.fillMaxWidth().padding(16.dp),
                                            verticalAlignment = Alignment.CenterVertically,
                                        ) {
                                            Text("□", style = MaterialTheme.typography.headlineSmall, color = MaterialTheme.colorScheme.primary)
                                            Column(modifier = Modifier.weight(1f).padding(start = 12.dp)) {
                                                MaterialText(name, fontWeight = FontWeight.SemiBold)
                                                Text("$count ${if (count == 1) "elemento" else "elementos"}", style = MaterialTheme.typography.bodySmall)
                                            }
                                            Text("›", style = MaterialTheme.typography.titleLarge)
                                        }
                                    }
                                }
                            }
                        }
                    }
                    HomeSection.ACTIVITY -> {
                        Column(
                            modifier = Modifier.weight(1f).verticalScroll(rememberScrollState()),
                            verticalArrangement = Arrangement.spacedBy(12.dp),
                        ) {
                        Card {
                            Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                                Text(if (isSyncing) "Sincronizando…" else "Sincronización automática", fontWeight = FontWeight.SemiBold)
                                Text(
                                    if (syncConfigured) lastSyncResult.ifBlank { "Los cambios se envían al guardarlos." }
                                    else "Conecta Windows para sincronizar tu biblioteca.",
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                                Button(
                                    onClick = { synchronizeDirect(showSuccess = true) },
                                    enabled = syncConfigured && !isSyncing,
                                ) { Text("Sincronizar ahora") }
                            }
                        }
                        if (conflicts.isNotEmpty()) {
                            Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.errorContainer)) {
                                Row(
                                    modifier = Modifier.fillMaxWidth().clickable { selectedConflict = conflicts.first() }.padding(16.dp),
                                    horizontalArrangement = Arrangement.SpaceBetween,
                                ) {
                                    Column { Text("Cambios simultáneos", fontWeight = FontWeight.SemiBold); Text("${conflicts.size} por revisar") }
                                    Text("Revisar")
                                }
                            }
                        }
                        }
                    }
                    HomeSection.SETTINGS -> {
                        Column(
                            modifier = Modifier.weight(1f).verticalScroll(rememberScrollState()),
                            verticalArrangement = Arrangement.spacedBy(10.dp),
                        ) {
                        Text("Datos y dispositivos", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                        OutlinedButton(onClick = { showSyncSettings = true }, modifier = Modifier.fillMaxWidth()) {
                            Text(if (syncConfigured) "Cambiar conexión con Windows" else "Conectar con Windows")
                        }
                        OutlinedButton(
                            onClick = { backupCreator.launch("unishare-respaldo.unishare.zip") },
                            modifier = Modifier.fillMaxWidth(),
                        ) { Text("Exportar copia de seguridad") }
                        OutlinedButton(
                            onClick = { backupPicker.launch(arrayOf("application/zip", "application/octet-stream")) },
                            modifier = Modifier.fillMaxWidth(),
                        ) { Text("Restaurar copia sin borrar datos") }
                        OutlinedButton(
                            onClick = { refreshOriginMetadata(automaticOnly = false) },
                            enabled = !isRefreshingOriginMetadata,
                            modifier = Modifier.fillMaxWidth(),
                        ) {
                            Text(if (isRefreshingOriginMetadata) "Analizando contenido…" else "Actualizar títulos y resúmenes")
                        }
                        OutlinedButton(
                            onClick = {
                                themePreference = themePreference.next()
                                ThemeSettings.save(this@MainActivity, themePreference)
                            },
                            modifier = Modifier.fillMaxWidth(),
                        ) { Text("Tema: ${themePreference.label}") }
                        OutlinedButton(
                            onClick = {
                                uiLanguage = if (uiLanguage == UiLanguageChoice.SPANISH) {
                                    UiLanguageChoice.ENGLISH
                                } else {
                                    UiLanguageChoice.SPANISH
                                }
                                UiStrings.current = uiLanguage
                                UiLanguageSettings.save(this@MainActivity, uiLanguage)
                            },
                            modifier = Modifier.fillMaxWidth(),
                        ) {
                            Text(if (uiLanguage == UiLanguageChoice.ENGLISH) "Language: English" else "Idioma: Español")
                        }
                        FilterChip(
                            selected = showArchived,
                            onClick = { showArchived = !showArchived; loadedItemLimit = 200; refresh() },
                            label = { Text(if (showArchived) "Volver a la biblioteca" else "Mostrar papelera") },
                        )
                        Spacer(Modifier.height(24.dp))
                        }
                    }
                }
            }
            }
        }

        if (captureExpanded) {
            ModalBottomSheet(onDismissRequest = { captureExpanded = false }) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .verticalScroll(rememberScrollState())
                        .imePadding()
                        .padding(horizontal = 20.dp)
                        .padding(bottom = 28.dp),
                    verticalArrangement = Arrangement.spacedBy(13.dp),
                ) {
                    Text("Guardar en UniShare", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.SemiBold)
                    Text(
                        "Pega un enlace y completaremos automáticamente el título y la información disponible.",
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    OutlinedTextField(
                        value = url,
                        onValueChange = { url = it },
                        label = { Text("Enlace http/https") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(14.dp),
                    )
                    OutlinedTextField(
                        value = quickCollection,
                        onValueChange = { quickCollection = it },
                        label = { Text("Carpeta (opcional)") },
                        supportingText = { Text("Se crea automáticamente y puedes cambiarla después.") },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(14.dp),
                    )
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        FilterChip(selected = !saveOffline, onClick = { saveOffline = false }, label = { Text("Solo enlace") })
                        FilterChip(selected = saveOffline, onClick = { saveOffline = true }, label = { Text("Copia offline") })
                    }
                    Button(
                        onClick = ::save,
                        enabled = !isSaving && url.isNotBlank(),
                        modifier = Modifier.fillMaxWidth(),
                    ) { Text(if (isSaving) "Guardando…" else "Guardar") }
                    OutlinedButton(onClick = chooseFile, modifier = Modifier.fillMaxWidth()) { Text("Elegir archivo…") }
                }
            }
        }

        selectedConflict?.let { conflict ->
            AlertDialog(
                onDismissRequest = { selectedConflict = null },
                title = { Text("Cambios simultáneos") },
                text = {
                    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text("UniShare conservó las dos versiones para evitar pérdida de datos.")
                        Text("Mi versión: ${conflict.localTitle}", fontWeight = FontWeight.SemiBold)
                        Text("Versión recibida: ${conflict.remoteTitle}", fontWeight = FontWeight.SemiBold)
                    }
                },
                confirmButton = {
                    TextButton(onClick = { resolveConflict(conflict, true) }) {
                        Text("Usar recibida")
                    }
                },
                dismissButton = {
                    Row {
                        TextButton(onClick = { resolveConflict(conflict, false) }) {
                            Text("Conservar la mía")
                        }
                        TextButton(onClick = { selectedConflict = null }) { Text("Cancelar") }
                    }
                },
            )
        }

        editingItem?.let { item ->
            AlertDialog(
                onDismissRequest = { editingItem = null },
                title = { Text("Editar información") },
                text = {
                    Column(
                        modifier = Modifier.verticalScroll(rememberScrollState()),
                        verticalArrangement = Arrangement.spacedBy(8.dp),
                    ) {
                        OutlinedButton(onClick = { openItem(item) }) { Text("Abrir contenido") }
                        if (item.originalUrl != null && item.assetSha256 == null) {
                            OutlinedButton(onClick = { retryOffline(item) }) {
                                Text("Guardar copia offline")
                            }
                        }
                        OutlinedTextField(
                            value = editingTitle,
                            onValueChange = { editingTitle = it },
                            label = { Text("Título") },
                            singleLine = true,
                        )
                        OutlinedTextField(
                            value = editingSource,
                            onValueChange = { editingSource = it },
                            label = { Text("Fuente") },
                            singleLine = true,
                        )
                        OutlinedTextField(
                            value = editingAuthor,
                            onValueChange = { editingAuthor = it },
                            label = { Text("Autor") },
                            singleLine = true,
                        )
                        Text(
                            "Resumen o notas",
                            style = MaterialTheme.typography.labelMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                        if (editingDescriptionUnlocked) {
                            OutlinedTextField(
                                value = editingDescription,
                                onValueChange = { editingDescription = it },
                                label = { Text("Editar resumen o notas") },
                                minLines = 3,
                            )
                            val summaryLinks = remember(editingDescription) {
                                SummaryLinkParser.find(editingDescription).distinctBy { it.url }
                            }
                            summaryLinks.forEach { link ->
                                TextButton(onClick = { openWebLink(link.url) }) {
                                    MaterialText(link.url, maxLines = 1)
                                }
                            }
                        } else {
                            if (editingDescription.isBlank()) {
                                Text(
                                    "Sin resumen disponible.",
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                            } else {
                                ClickableSummaryText(editingDescription)
                            }
                            OutlinedButton(onClick = { editingDescriptionUnlocked = true }) {
                                Text("Modificar resumen")
                            }
                        }
                        OutlinedTextField(
                            value = editingTags,
                            onValueChange = { editingTags = it },
                            label = { Text("Etiquetas separadas por comas") },
                        )
                        OutlinedTextField(
                            value = editingCollections,
                            onValueChange = { editingCollections = it },
                            label = { Text("Colecciones separadas por comas") },
                        )
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Checkbox(
                                checked = editingFavorite,
                                onCheckedChange = { editingFavorite = it },
                                modifier = Modifier.semantics {
                                    contentDescription = UiStrings.translate("Marcar como favorito")
                                },
                            )
                            Text("Marcar como favorito")
                        }
                    }
                },
                confirmButton = {
                    TextButton(onClick = ::saveEditedItem) { Text("Guardar") }
                },
                dismissButton = {
                    Row {
                        if (item.deletedAt == null) {
                            TextButton(
                                onClick = {
                                    editingItem = null
                                    pendingDeletion = item
                                },
                                colors = ButtonDefaults.textButtonColors(contentColor = MaterialTheme.colorScheme.error),
                            ) {
                                Text("Eliminar")
                            }
                        } else {
                            TextButton(onClick = { setArchived(item, false) }) {
                                Text("Restaurar")
                            }
                        }
                        TextButton(onClick = { editingItem = null }) { Text("Cancelar") }
                    }
                },
            )
        }

        pendingDeletion?.let { item ->
            AlertDialog(
                onDismissRequest = { pendingDeletion = null },
                title = { Text("¿Eliminar este elemento?") },
                text = {
                    Text(
                        "«${item.title}» se moverá a la Papelera y el cambio se sincronizará. " +
                            "Podrás restaurarlo después.",
                    )
                },
                confirmButton = {
                    TextButton(
                        onClick = {
                            pendingDeletion = null
                            setArchived(item, true)
                        },
                        colors = ButtonDefaults.textButtonColors(contentColor = MaterialTheme.colorScheme.error),
                    ) {
                        Text("Eliminar")
                    }
                },
                dismissButton = {
                    TextButton(onClick = { pendingDeletion = null }) { Text("Cancelar") }
                },
            )
        }

        if (pendingBulkDeletion) {
            val activeCount = items.count { it.id in selectedItemIds && it.deletedAt == null }
            AlertDialog(
                onDismissRequest = { pendingBulkDeletion = false },
                title = { Text("¿Eliminar $activeCount elementos?") },
                text = {
                    Text(
                        "Se moverán a la Papelera y los cambios se sincronizarán. " +
                            "Podrás restaurarlos después.",
                    )
                },
                confirmButton = {
                    TextButton(
                        onClick = {
                            pendingBulkDeletion = false
                            setSelectedArchived(true)
                        },
                        colors = ButtonDefaults.textButtonColors(contentColor = MaterialTheme.colorScheme.error),
                    ) {
                        Text("Eliminar")
                    }
                },
                dismissButton = {
                    TextButton(onClick = { pendingBulkDeletion = false }) { Text("Cancelar") }
                },
            )
        }

        if (showSyncSettings) {
            AlertDialog(
                onDismissRequest = { showSyncSettings = false },
                title = { Text("Sincronización directa") },
                text = {
                    Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                        Text("Conecta Android y Windows a la misma red de Tailscale.")
                        Text(
                            "Lo más sencillo es cerrar esta ventana y escanear el QR que muestra UniShare en Windows.",
                            style = MaterialTheme.typography.bodySmall,
                        )
                        OutlinedTextField(
                            value = syncEndpoint,
                            onValueChange = { syncEndpoint = it },
                            label = { Text("Dirección HTTPS .ts.net") },
                            singleLine = true,
                        )
                        OutlinedTextField(
                            value = syncKey,
                            onValueChange = { syncKey = it },
                            label = { Text("Clave de emparejamiento") },
                            singleLine = true,
                        )
                        Text(
                            "Los cambios se envían al guardarlos. Si el ordenador está apagado, quedan " +
                                "pendientes y se reintentan automáticamente.",
                            style = MaterialTheme.typography.bodySmall,
                        )
                    }
                },
                confirmButton = {
                    TextButton(onClick = ::saveSyncSettings) { Text("Guardar y conectar") }
                },
                dismissButton = {
                    TextButton(onClick = { showSyncSettings = false }) { Text("Cancelar") }
                },
            )
        }

        pendingRestoreUri?.let { backupUri ->
            AlertDialog(
                onDismissRequest = { pendingRestoreUri = null },
                title = { Text("Incorporar copia de seguridad") },
                text = {
                    Text(
                        "UniShare verificará la copia y fusionará sus elementos sin borrar los datos " +
                            "actuales. Los cambios simultáneos quedarán como conflictos para que los revises.",
                    )
                },
                confirmButton = {
                    TextButton(onClick = {
                        pendingRestoreUri = null
                        restoreBackup(backupUri)
                    }) { Text("Verificar e incorporar") }
                },
                dismissButton = {
                    TextButton(onClick = { pendingRestoreUri = null }) { Text("Cancelar") }
                },
            )
        }
    }
}

@Composable
private fun UniShareTheme(preference: ThemePreference, content: @Composable () -> Unit) {
    val dark = when (preference) {
        ThemePreference.SYSTEM -> isSystemInDarkTheme()
        ThemePreference.LIGHT -> false
        ThemePreference.DARK -> true
    }
    val colors = if (dark) {
        darkColorScheme(
            primary = Color(0xFFB8ACFF),
            onPrimary = Color(0xFF24165B),
            primaryContainer = Color(0xFF302868),
            onPrimaryContainer = Color(0xFFE9E5FF),
            secondary = Color(0xFFC8C2DC),
            secondaryContainer = Color(0xFF302F45),
            tertiary = Color(0xFF57D3D9),
            background = Color(0xFF0C111A),
            onBackground = Color(0xFFF3F5FB),
            surface = Color(0xFF131A25),
            onSurface = Color(0xFFF3F5FB),
            surfaceVariant = Color(0xFF222C3B),
            onSurfaceVariant = Color(0xFFAEB8C9),
            outline = Color(0xFF3A4658),
            error = Color(0xFFFF8D86),
        )
    } else {
        lightColorScheme(
            primary = Color(0xFF6554D9),
            onPrimary = Color.White,
            primaryContainer = Color(0xFFECEAFF),
            onPrimaryContainer = Color(0xFF24165B),
            secondary = Color(0xFF5D5874),
            secondaryContainer = Color(0xFFE8E4F5),
            tertiary = Color(0xFF167A82),
            background = Color(0xFFF5F7FB),
            onBackground = Color(0xFF172033),
            surface = Color.White,
            onSurface = Color(0xFF172033),
            surfaceVariant = Color(0xFFEEF2F8),
            onSurfaceVariant = Color(0xFF5A6478),
            outline = Color(0xFFD3D9E5),
            error = Color(0xFFB42318),
        )
    }
    MaterialTheme(colorScheme = colors, content = content)
}
