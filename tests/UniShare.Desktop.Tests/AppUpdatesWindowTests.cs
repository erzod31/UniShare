using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UniShare.Application;
using UniShare.Desktop;
using UniShare.Infrastructure;

namespace UniShare.Desktop.Tests;

public sealed class AppUpdatesWindowTests
{
    [Fact]
    public void UpdateWindowChecksDownloadsAndEnablesConfirmationWithoutLaunchingAnything()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "unishare-update-ui-" + Guid.NewGuid().ToString("N"));
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                using var handler = new FakeUpdater();
                using var http = new HttpClient(handler);
                var service = new GitHubAppUpdates(http, "erzod31/UniShare", "0.6.24", root, handler, handler);
                using var window = new AppUpdatesWindow(root, service);
                var check = (Button)window.FindName("CheckButton");
                var download = (Button)window.FindName("DownloadButton");
                var install = (Button)window.FindName("InstallButton");
                Assert.False(download.IsEnabled);
                Assert.False(install.IsEnabled);
                check.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => check.IsEnabled);
                Assert.True(download.IsEnabled);
                Assert.Contains("0.6.25", ((TextBlock)window.FindName("StatusText")).Text, StringComparison.Ordinal);
                download.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => check.IsEnabled);
                Assert.True(install.IsEnabled);
                Assert.Equal(1, handler.Verifications);
                Assert.Equal(0, handler.NativeRequests);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (Path.GetFullPath(root).StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Directory.Exists(root)) { Directory.Delete(root, true); }
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Update window did not finish.");
        if (failure is not null) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw(); }
    }

    private static void PumpUntil(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (done() || DateTime.UtcNow >= deadline) { frame.Continue = false; } };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Assert.True(done(), "Update UI timed out.");
    }

    private sealed class FakeUpdater : HttpMessageHandler, IAppUpdatePackageVerifier, IAppUpdateInstaller
    {
        private static readonly byte[] Package = "synthetic, never executed"u8.ToArray();
        public int Verifications { get; private set; }
        public int NativeRequests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = request.RequestUri!.Host == "api.github.com" ? JsonSerializer.SerializeToUtf8Bytes(new
            {
                draft = false,
                prerelease = false,
                tag_name = "v0.6.25",
                html_url = "https://github.com/erzod31/UniShare/releases/tag/v0.6.25",
                assets = new[] { new { name = "UniShare-Windows-Setup-0.6.25.exe", state = "uploaded", size = Package.Length,
                    digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Package)),
                    browser_download_url = "https://github.com/erzod31/UniShare/releases/download/v0.6.25/UniShare-Windows-Setup-0.6.25.exe" } },
            }) : Package;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) });
        }
        public Task<string> VerifyAsync(string path, AppUpdateCandidate candidate, CancellationToken cancellationToken)
        {
            Verifications++;
            return Task.FromResult("legacy-unsigned");
        }
        public Task RequestAsync(VerifiedAppUpdate update, CancellationToken cancellationToken)
        {
            NativeRequests++;
            throw new InvalidOperationException("UI test must never request a native installer.");
        }
    }
}
