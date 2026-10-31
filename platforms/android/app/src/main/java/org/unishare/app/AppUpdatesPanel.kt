package org.unishare.app

import android.app.Activity
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File
import java.util.concurrent.CancellationException

@Composable
internal fun AppUpdatesPanel(activity: Activity, language: UiLanguageChoice) {
    fun text(es: String, en: String) = if (language == UiLanguageChoice.ENGLISH) en else es
    val scope = rememberCoroutineScope()
    var busy by remember { mutableStateOf(false) }
    var status by remember { mutableStateOf("") }
    var candidate by remember { mutableStateOf<AppUpdateCandidate?>(null) }
    var ready by remember { mutableStateOf<VerifiedAppUpdate?>(null) }
    var progress by remember { mutableFloatStateOf(0f) }
    var operation by remember { mutableStateOf<AppUpdateCancellation?>(null) }
    var confirm by remember { mutableStateOf(false) }
    val channel = remember(activity) {
        runCatching {
            activity.assets.open("updates/channel.json").bufferedReader().use { JSONObject(it.readText()) }.also {
                require(it.getInt("schemaVersion") == 1 && it.getString("productId") == "unishare" && it.getString("channel") == "stable")
                AppUpdatePolicy.validateRepository(it.getString("repository"))
            }
        }.getOrNull()
    }
    val repository = channel?.optString("repository") ?: ""
    val productionPackage = channel?.optString("androidPackage") ?: ""
    val client = remember(activity, repository) {
        AppUpdateClient(repository, BuildConfig.VERSION_NAME, File(activity.cacheDir, "app-updates"),
            AndroidAppUpdateVerifier(activity, productionPackage), AndroidAppUpdateInstaller(activity))
    }
    DisposableEffect(activity) { onDispose { operation?.cancel() } }

    fun run(action: suspend (AppUpdateCancellation) -> Unit) {
        if (busy) return
        val token = AppUpdateCancellation()
        operation = token
        busy = true
        scope.launch {
            status = text("Comprobando o descargando…", "Checking or downloading…")
            try { action(token) }
            catch (_: CancellationException) { status = text("Operación cancelada. No se instaló nada.", "Operation canceled. Nothing was installed.") }
            catch (_: Exception) {
                ready = null
                status = text("No se pudo verificar la actualización. Comprueba la red y vuelve a intentarlo; no se instaló nada.",
                    "The update could not be verified. Check the network and retry; nothing was installed.")
            } finally { operation = null; busy = false }
        }
    }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(text("Actualizar UniShare", "Update UniShare"), style = MaterialTheme.typography.titleMedium)
            Text(text("Versión instalada: ", "Installed version: ") + BuildConfig.VERSION_NAME)
            Text("GitHub Releases · $repository", style = MaterialTheme.typography.bodySmall)
            if (channel == null) Text(text("Canal de actualización no válido.", "Invalid update channel."))
            if (activity.packageName != productionPackage) Text(text("Compilación QA: no se instalarán paquetes de producción.", "QA build: production packages will not be installed."))
            if (status.isNotBlank()) Text(status)
            if (busy) LinearProgressIndicator(progress = { progress.coerceIn(0f, 1f) }, modifier = Modifier.fillMaxWidth())
            OutlinedButton(enabled = !busy && channel != null, onClick = {
                run { token ->
                    candidate = null; ready = null; progress = 0f
                    val result = withContext(Dispatchers.IO) { client.check(token) }
                    candidate = result.candidate
                    status = if (result.candidate != null) text("Nueva versión: ", "New version: ") + result.candidate.version
                    else if (result.published != null) text("Publicada en el canal: ", "Published on the channel: ") + result.published +
                        text(". No se ofrece una versión anterior.", ". An older version will not be offered.")
                    else text("Todavía no hay una publicación estable.", "There is no stable release yet.")
                }
            }) { Text(text("Buscar actualización", "Check for updates")) }
            if (candidate != null && ready == null) Button(enabled = !busy && activity.packageName == productionPackage, onClick = {
                val next = candidate ?: return@Button
                run { token ->
                    ready = withContext(Dispatchers.IO) { client.download(next, token) { progress = it.toFloat() / next.bytes } }
                    status = text("APK verificado: tamaño, SHA-256, producto, versión y certificado coinciden.",
                        "APK verified: size, SHA-256, product, version and certificate match.")
                }
            }) { Text(text("Descargar actualización", "Download update")) }
            if (ready != null) Button(enabled = !busy, onClick = { confirm = true }) {
                Text(text("Instalar actualización", "Install update"))
            }
            if (busy) TextButton(onClick = { operation?.cancel() }) { Text(text("Cancelar", "Cancel")) }
        }
    }
    if (confirm) AlertDialog(
        onDismissRequest = { confirm = false },
        title = { Text(text("Actualizar UniShare", "Update UniShare")) },
        text = { Text(text("Android pedirá permiso para instalar desde UniShare y confirmación para actualizar. No desinstales la app: se conservan tus datos. Al terminar, pulsa Abrir en el instalador.",
            "Android will request permission to install from UniShare and confirmation to update. Keep the app installed to preserve your data. When finished, select Open in the installer.")) },
        dismissButton = { TextButton(onClick = { confirm = false }) { Text(text("Cancelar", "Cancel")) } },
        confirmButton = { TextButton(onClick = {
            confirm = false
            val update = ready ?: return@TextButton
            run { token ->
                // Hash and package are checked on IO, while native UI is requested on the main thread.
                withContext(Dispatchers.IO) { client.verifyReady(update, token) }
                token.check()
                val result = AndroidAppUpdateInstaller(activity).request(update)
                status = if (result == AppUpdateInstallResult.PERMISSION_REQUIRED)
                    text("Concede el permiso y vuelve a pulsar Instalar actualización.", "Grant permission, then select Install update again.")
                else text("Actualización solicitada a Android. Confirma en el instalador; al terminar puedes abrir UniShare.",
                    "Update requested from Android. Confirm in the installer; you can open UniShare when it finishes.")
            }
        }) { Text(text("Continuar", "Continue")) } },
    )
}
