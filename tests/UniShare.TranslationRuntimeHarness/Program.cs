using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using UniShare.Desktop;

namespace UniShare.TranslationRuntimeHarness;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1 || Directory.Exists(args[0]))
        {
            Console.Error.WriteLine("Pass a new, isolated QA directory. Network is used only for public models.");
            return 2;
        }
        var root = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(root);
        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exit = 1;
        var window = new Window { Width = 500, Height = 300, Title = "UniShare isolated translation QA", ShowActivated = false, ShowInTaskbar = false };
        var panel = new StackPanel();
        window.Content = panel;
        window.Loaded += async (_, _) =>
        {
            try
            {
                var results = new List<object>();
                var cases = new[]
                {
                    (Text: "This is a practical guide to organizing your personal library. You can save links and files without an internet connection. Visit https://example.com/guide?a=1&b=2 🌍 #Obsidian", Target: "es", Source: "en", Word: "biblioteca"),
                    (Text: "Esta es una guía práctica para organizar tu biblioteca personal. Puedes guardar enlaces y archivos sin conexión a Internet. Visita https://example.com/guide?a=1&b=2 🌍 #Obsidian", Target: "en", Source: "es", Word: "library"),
                    (Text: "Ce guide explique comment organiser votre bibliothèque personnelle. Vous pouvez enregistrer des liens et des fichiers sans connexion Internet.", Target: "es", Source: "fr", Word: "biblioteca"),
                };
                foreach (var offline in new[] { false, true })
                {
                    var view = new WebView2 { Width = 1, Height = 1, Visibility = Visibility.Hidden };
                    panel.Children.Add(view);
                    using var translator = new LocalDescriptionTranslator(view, root, offline ? new RejectNetworkHandler() : null);
                    if (offline)
                    {
                        // Exercise fresh inference from the downloaded models, not a previous result.
                        var cache = Path.Combine(root, "cache", "translation", "results");
                        foreach (var file in Directory.GetFiles(cache, "*.json")) File.Move(file, file + ".online-proof");
                    }
                    foreach (var item in cases)
                    {
                        var result = await translator.TranslateAsync(item.Text, item.Target);
                        Require(result.IsTranslated && result.Source == item.Source && result.Target == item.Target, $"Inference failed ({item.Source}-{item.Target}, offline={offline}): {result.Status}");
                        Require(result.Text.Contains(item.Word, StringComparison.OrdinalIgnoreCase), "Expected translated vocabulary missing");
                        if (item.Text.Contains("https://", StringComparison.Ordinal))
                        {
                            Require(result.Text.Contains("https://example.com/guide?a=1&b=2 🌍 #Obsidian", StringComparison.Ordinal), "Literal link/emoji/hashtag changed");
                        }
                        results.Add(new { offline, result.Source, result.Target, result.Status, result.Text });
                    }
                    var same = await translator.TranslateAsync(cases[0].Text, "en");
                    Require(same.Status == "original" && same.Text == cases[0].Text, "Same-language text changed");
                    var symbols = await translator.TranslateAsync("🌍 #tag https://example.com/", "es");
                    Require(symbols.Text == "🌍 #tag https://example.com/", "Non-prose text changed");
                    if (offline)
                    {
                        DescriptionTranslation.Initialize(translator, root);
                        var reading = new TranslatedDescriptionBlock { ContentText = cases[0].Text };
                        panel.Children.Add(reading);
                        var children = (StackPanel)reading.Content;
                        var rendered = (LinkifiedTextBlock)children.Children[0];
                        var originalButton = (Button)children.Children[2];
                        var deadline = DateTime.UtcNow.AddSeconds(15);
                        while (!rendered.ContentText!.Contains("biblioteca", StringComparison.OrdinalIgnoreCase) && DateTime.UtcNow < deadline)
                        {
                            await Task.Delay(50);
                        }
                        Require(rendered.ContentText!.Contains("biblioteca", StringComparison.OrdinalIgnoreCase), "In-app reading view did not translate automatically");
                        originalButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Require(rendered.ContentText == cases[0].Text, "Show original did not preserve the exact source");
                        DescriptionTranslation.Toggle();
                        Require(!DescriptionTranslation.Enabled && rendered.ContentText == cases[0].Text, "Disabling translation did not restore the original");
                        DescriptionTranslation.Toggle();
                        panel.Children.Remove(reading);
                        reading.Dispose();
                        results.Add(new { inAppView = true, originalToggle = true, preferenceToggle = true });
                    }
                    panel.Children.Remove(view);
                }
                var blockedView = new WebView2 { Width = 1, Height = 1, Visibility = Visibility.Hidden };
                panel.Children.Add(blockedView);
                using (var blocker = new BlockingNetworkHandler())
                using (var closing = new LocalDescriptionTranslator(blockedView, Path.Combine(root, "cancel-profile"), blocker))
                {
                    var request = closing.TranslateAsync(cases[0].Text, "es");
                    await blocker.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    closing.Dispose();
                    try { _ = await request.WaitAsync(TimeSpan.FromSeconds(10)); }
                    catch (OperationCanceledException) { }
                    await Task.Delay(100);
                    results.Add(new { cancelledDuringDownload = true, applicationResponsive = true });
                }
                panel.Children.Remove(blockedView);
                await File.WriteAllTextAsync(Path.Combine(root, "results.json"), JsonSerializer.Serialize(results, JsonOptions));
                Console.WriteLine($"PASS: {cases.Length * 2} real Windows translations, online and offline; reading view, toggles, links and originals verified.");
                exit = 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
            }
            finally
            {
                window.Close();
                application.Shutdown();
            }
        };
        window.Show();
        application.Run();
        return exit;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    private sealed class RejectNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("All model network requests are deliberately blocked in the offline test.");
    }

    private sealed class BlockingNetworkHandler : HttpMessageHandler
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The blocked request must be cancelled.");
        }
    }
}
