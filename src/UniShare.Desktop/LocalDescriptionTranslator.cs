using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using UniShare.Application;
using UniShare.Infrastructure;

namespace UniShare.Desktop;

/// <summary>Hosts trusted local WASM in a sandboxed worker. The host downloads model hashes only.</summary>
internal sealed class LocalDescriptionTranslator : IDescriptionTranslator, IDisposable
{
    private const string Origin = "https://unishare-translation.invalid";
    private static readonly HashSet<string> Assets = new(StringComparer.Ordinal)
    {
        "index.html", "bridge.js", "worker.js", "policy.js", "models.json",
        "vendor/bergamot-translator.js", "vendor/bergamot-translator.wasm", "vendor/fzstd.js", "vendor/language-detection.js",
    };
    private readonly WebView2 _view;
    private readonly string _assetRoot = Path.Combine(AppContext.BaseDirectory, "translation");
    private readonly string _webProfile;
    private readonly TranslationAssetCache _models;
    private readonly DescriptionTranslationCache _cache;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<string, TaskCompletionSource<DescriptionTranslationResult>> _pending = [];
    private Task? _initialization;
    private TaskCompletionSource? _ready;
    private bool _disposed;

    internal LocalDescriptionTranslator(WebView2 view, string profileRoot, HttpMessageHandler? modelHandler = null)
    {
        _view = view;
        var root = Path.Combine(profileRoot, "cache", "translation");
        _webProfile = Path.Combine(root, "webview");
        _cache = new DescriptionTranslationCache(Path.Combine(root, "results"));
        _models = new TranslationAssetCache(File.ReadAllText(Path.Combine(_assetRoot, "models.json")),
            Path.Combine(root, "models"), modelHandler);
    }

    public async Task<DescriptionTranslationResult> TranslateAsync(string text, string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        _ = DescriptionTranslationCache.Key(text, targetLanguage);
        var original = new DescriptionTranslationResult("unavailable", text, string.Empty, targetLanguage);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        var token = linked.Token;
        try
        {
            var cached = await _cache.ReadAsync(text, targetLanguage, token);
            if (cached is not null)
            {
                return cached;
            }
            await _gate.WaitAsync(token);
            try
            {
                cached = await _cache.ReadAsync(text, targetLanguage, token);
                if (cached is not null)
                {
                    return cached;
                }
                token.ThrowIfCancellationRequested();
                await (_initialization ??= InitializeAsync()).WaitAsync(token);
                var id = Guid.NewGuid().ToString("N");
                var completion = new TaskCompletionSource<DescriptionTranslationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending.Add(id, completion);
                try
                {
                    await _view.ExecuteScriptAsync($"window.uniShareTranslate({JsonSerializer.Serialize(new { id, text, target = targetLanguage })})");
                    var result = await completion.Task.WaitAsync(TimeSpan.FromMinutes(5), token);
                    await _cache.WriteAsync(text, result, token);
                    return result;
                }
                finally
                {
                    _pending.Remove(id);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException or
            NotImplementedException or InvalidCastException or
            System.Runtime.InteropServices.COMException or WebView2RuntimeNotFoundException)
        {
            return original;
        }
    }

    private async Task InitializeAsync()
    {
        _ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _webProfile);
        await _view.EnsureCoreWebView2Async(environment);
        var core = _view.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.NavigationStarting += (_, e) => e.Cancel = e.Uri != Origin + "/index.html";
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All,
            CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += ResourceRequested;
        core.WebMessageReceived += MessageReceived;
        core.ProcessFailed += (_, _) => FailPending();
        core.Navigate(Origin + "/index.html");
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(30), _shutdown.Token);
    }

    private void FailPending()
    {
        _ready?.TrySetException(new InvalidOperationException("Local translation engine stopped."));
        foreach (var pending in _pending.Values)
        {
            pending.TrySetException(new InvalidOperationException("Local translation engine stopped."));
        }
    }

    private async void ResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        using var deferral = e.GetDeferral();
        try
        {
            var uri = new Uri(e.Request.Uri);
            if (uri.GetLeftPart(UriPartial.Authority) != Origin || !string.IsNullOrEmpty(uri.Query))
            {
                throw new InvalidDataException("Only local translation assets are allowed.");
            }
            var name = uri.AbsolutePath.TrimStart('/');
            var file = name.StartsWith("models/", StringComparison.Ordinal)
                ? await _models.GetAsync(name[7..], _shutdown.Token)
                : Assets.Contains(name) ? Path.Combine(_assetRoot, name.Replace('/', Path.DirectorySeparatorChar))
                    : throw new InvalidDataException("Unknown local translation asset.");
            var mime = Path.GetExtension(name) switch
            {
                ".html" => "text/html; charset=utf-8",
                ".js" => "application/javascript; charset=utf-8",
                ".json" => "application/json",
                ".wasm" => "application/wasm",
                _ => "application/octet-stream",
            };
            var bytes = await File.ReadAllBytesAsync(file, _shutdown.Token);
            e.Response = _view.CoreWebView2.Environment.CreateWebResourceResponse(
                new MemoryStream(bytes, writable: false), 200, "OK", $"Content-Type: {mime}\r\nCache-Control: no-store");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            if (!_disposed && _view.CoreWebView2 is { } core)
            {
                e.Response = core.Environment.CreateWebResourceResponse(Stream.Null, 503, "Unavailable", string.Empty);
            }
        }
    }

    private void MessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(Origin + "/", StringComparison.Ordinal))
        {
            return;
        }
        try
        {
            var message = e.TryGetWebMessageAsString();
            if (message.Length > 1_600_000)
            {
                return;
            }
            using var document = JsonDocument.Parse(message);
            var json = document.RootElement;
            var type = json.GetProperty("type").GetString();
            if (type == "ready")
            {
                _ready?.TrySetResult();
            }
            else if (type == "result" && json.TryGetProperty("id", out var id) &&
                     _pending.TryGetValue(id.GetString() ?? string.Empty, out var pending))
            {
                pending.TrySetResult(new DescriptionTranslationResult(json.GetProperty("status").GetString()!,
                    json.GetProperty("text").GetString()!, json.GetProperty("source").GetString()!, json.GetProperty("target").GetString()!));
            }
            else if (type == "fatal")
            {
                FailPending();
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // Ignore malformed messages; timeout still falls back to the original.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _shutdown.Cancel();
        _view.Dispose();
        _models.Dispose();
        _gate.Dispose();
        _shutdown.Dispose();
    }
}
