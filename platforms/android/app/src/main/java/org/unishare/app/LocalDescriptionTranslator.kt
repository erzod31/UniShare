package org.unishare.app

import android.annotation.SuppressLint
import android.content.Context
import android.graphics.Bitmap
import android.view.View
import android.webkit.JavascriptInterface
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebView
import android.webkit.WebViewClient
import android.webkit.RenderProcessGoneDetail
import androidx.core.content.edit
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import org.json.JSONObject
import java.io.ByteArrayInputStream
import java.io.File
import java.util.UUID

internal object DescriptionTranslationSettings {
    fun load(context: Context): Boolean = context.getSharedPreferences("description_translation", Context.MODE_PRIVATE).getBoolean("enabled", true)
    fun save(context: Context, enabled: Boolean) {
        context.getSharedPreferences("description_translation", Context.MODE_PRIVATE).edit { putBoolean("enabled", enabled) }
    }
}

/** Trusted local HTML/WASM only; native networking accepts catalog model hashes, never text. */
@SuppressLint("SetJavaScriptEnabled")
internal class LocalDescriptionTranslator(
    private val context: Context,
    private val root: File = File(context.cacheDir, "translation"),
    private val models: TranslationModelCache = TranslationModelCache(
        context.assets.open("translation/models.json").bufferedReader().use { it.readText() }, File(root, "models")),
) : AutoCloseable {
    private val cache = DescriptionTranslationCache(File(root, "results"))
    private val gate = Mutex()
    private var ready = CompletableDeferred<Unit>()
    private val pending = mutableMapOf<String, CompletableDeferred<DescriptionTranslationResult>>()
    private var started = false
    private var closed = false
    val view: WebView = WebView(context).apply {
        settings.javaScriptEnabled = true
        settings.allowFileAccess = false
        settings.allowContentAccess = false
        settings.domStorageEnabled = false
        settings.setSupportMultipleWindows(false)
        importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO_HIDE_DESCENDANTS
        isFocusable = false
        isClickable = false
        addJavascriptInterface(Bridge(), "UniShareNative")
        webViewClient = object : WebViewClient() {
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean = true
            override fun shouldInterceptRequest(view: WebView, request: WebResourceRequest): WebResourceResponse = resource(request)
            override fun onRenderProcessGone(view: WebView, detail: RenderProcessGoneDetail): Boolean {
                val failure = IllegalStateException("Local translation engine stopped")
                ready.completeExceptionally(failure)
                pending.values.forEach { it.completeExceptionally(failure) }
                return true
            }
            override fun onPageStarted(view: WebView, url: String, favicon: Bitmap?) {
                if (url != ORIGIN + "/index.html") view.stopLoading()
            }
        }
    }

    suspend fun translate(text: String, target: String): DescriptionTranslationResult {
        DescriptionTranslationCache.key(text, target)
        val original = DescriptionTranslationResult("unavailable", text, "", target)
        return withContext(Dispatchers.Main.immediate) {
            try {
                withContext(Dispatchers.IO) { cache.read(text, target) }?.let { return@withContext it }
                gate.withLock {
                    withContext(Dispatchers.IO) { cache.read(text, target) }?.let { return@withLock it }
                    check(!closed)
                    if (!started) { started = true; view.loadUrl(ORIGIN + "/index.html") }
                    withTimeout(30_000) { ready.await() }
                    val id = UUID.randomUUID().toString()
                    val completion = CompletableDeferred<DescriptionTranslationResult>()
                    pending[id] = completion
                    try {
                        val request = JSONObject().put("id", id).put("text", text).put("target", target)
                        view.evaluateJavascript("window.uniShareTranslate($request)", null)
                        val result = withTimeout(300_000) { completion.await() }
                        withContext(Dispatchers.IO) { cache.write(text, result) }
                        result
                    } finally { pending.remove(id) }
                }
            } catch (_: kotlinx.coroutines.TimeoutCancellationException) { original }
            catch (exception: kotlinx.coroutines.CancellationException) { throw exception }
            catch (_: Exception) { original }
        }
    }

    private fun resource(request: WebResourceRequest): WebResourceResponse {
        try {
            val uri = request.url
            require(uri.scheme == "https" && uri.host == "unishare-translation.invalid" && uri.query == null && uri.port == -1)
            val name = uri.path.orEmpty().removePrefix("/")
            val input = if (name.startsWith("models/")) models.get(name.removePrefix("models/")).inputStream()
                else { require(name in ASSETS); context.assets.open("translation/$name") }
            val mime = when (name.substringAfterLast('.')) {
                "html" -> "text/html"
                "js" -> "application/javascript"
                "json" -> "application/json"
                "wasm" -> "application/wasm"
                else -> "application/octet-stream"
            }
            return WebResourceResponse(mime, "UTF-8", input)
        } catch (_: Exception) {
            return WebResourceResponse("text/plain", "UTF-8", 503, "Unavailable", emptyMap(), ByteArrayInputStream(ByteArray(0)))
        }
    }

    private inner class Bridge {
        @JavascriptInterface
        fun postMessage(message: String) {
            if (message.length > 1_600_000) return
            view.post {
                if (closed) return@post
                runCatching {
                    val json = JSONObject(message)
                    when (json.getString("type")) {
                        "ready" -> ready.complete(Unit)
                        "result" -> pending[json.getString("id")]?.complete(DescriptionTranslationResult(
                            json.getString("status"), json.getString("text"), json.getString("source"), json.getString("target")))
                        "fatal" -> pending.values.forEach { it.completeExceptionally(IllegalStateException("Local engine stopped")) }
                    }
                }
            }
        }
    }

    override fun close() {
        if (closed) return
        closed = true
        pending.values.forEach { it.cancel() }
        ready.cancel()
        models.close()
        (view.parent as? android.view.ViewGroup)?.removeView(view)
        view.removeJavascriptInterface("UniShareNative")
        view.stopLoading()
        view.destroy()
    }

    companion object {
        private const val ORIGIN = "https://unishare-translation.invalid"
        private val ASSETS = setOf("index.html", "bridge.js", "worker.js", "policy.js", "models.json",
            "vendor/bergamot-translator.js", "vendor/bergamot-translator.wasm", "vendor/fzstd.js", "vendor/language-detection.js")
    }
}
