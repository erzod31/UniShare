using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using UniShare.Application;
using UniShare.Infrastructure;
using Forms = System.Windows.Forms;

namespace UniShare.Desktop;

public partial class App : System.Windows.Application, IDisposable
{
    private const string MutexName = @"Local\UniShare.Desktop.SingleInstance";
    private const string ActivationEventName = @"Local\UniShare.Desktop.ShowWindow";

    private IDownloadService? _downloads;
    private DirectSyncServer? _directSyncServer;
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;
    private Forms.NotifyIcon? _trayIcon;
    private Icon? _trayIconImage;
    private bool _ownsMutex;
    private bool _exitRequested;
    private bool _exitOnClose;
    private bool _shutdownStarted;
    private bool _closeTipShown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            UiLanguage.Initialize();
            var smokeTest = e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
            var allowMultiple = e.Args.Contains("--allow-multiple", StringComparer.OrdinalIgnoreCase);
            var waitForPrevious = e.Args.Contains("--wait-for-previous", StringComparer.OrdinalIgnoreCase);
            var skipTailscale = e.Args.Contains("--skip-tailscale", StringComparer.OrdinalIgnoreCase);
            _exitOnClose = e.Args.Contains("--exit-on-close", StringComparer.OrdinalIgnoreCase);
            if (!smokeTest && !allowMultiple && !TryBecomePrimaryInstance(waitForPrevious))
            {
                Shutdown(0);
                return;
            }

            DesktopTheme.LoadAndApply(this);
            var profileRoot = ResolveProfilePath(e.Args);
            Directory.CreateDirectory(profileRoot);
            var repository = new SqliteLibraryRepository(Path.Combine(profileRoot, "library.db"));
            var blobStore = new FileBlobStore(profileRoot);
            var library = new LibraryService(repository, blobStore, TimeProvider.System);
            var backup = new PortableBackupService(profileRoot);
            var sync = new PortableSyncService(profileRoot, backup, blobStore);
            var obsidian = new ObsidianVaultService(repository, blobStore, TimeProvider.System);
            _directSyncServer = new DirectSyncServer(profileRoot, backup, sync, library: library);
            _downloads = new DurableDownloadService(
                repository, blobStore, new PublicInternetEndpointPolicy(), profileRoot, TimeProvider.System);
            await library.InitializeAsync();
            _ = await new BlobGarbageCollector(profileRoot, TimeProvider.System)
                .CollectAsync(TimeSpan.FromDays(7));

            string? directSyncError = null;
            string? tailscaleSetupError = null;
            try
            {
                await _directSyncServer.StartAsync();
                if (!smokeTest && !skipTailscale)
                {
                    tailscaleSetupError = await TailscaleDirectSync.TryConfigureAsync(_directSyncServer);
                }
            }
            catch (Exception exception)
            {
                directSyncError = exception.Message;
            }

            if (smokeTest)
            {
                if (directSyncError is not null || !_directSyncServer.IsRunning)
                {
                    await _directSyncServer.DisposeAsync();
                    _directSyncServer = null;
                    Shutdown(2);
                    return;
                }
                _ = await library.SearchAsync(query: null, includeDeleted: true);
                await _directSyncServer.DisposeAsync();
                _directSyncServer = null;
                Shutdown(0);
                return;
            }

            var window = new MainWindow(
                library, blobStore, backup, sync, obsidian, _downloads, _directSyncServer,
                directSyncError, tailscaleSetupError, profileRoot);
            MainWindow = window;
            window.Closing += MainWindow_Closing;
            window.Closed += MainWindow_Closed;
            CreateTrayIcon();
            if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
            {
                ShowMainWindow();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                UiLanguage.T($"UniShare no pudo iniciar.\n\n{exception.Message}"),
                UiLanguage.T("Error de inicio"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Windows Restart Manager uses the session-ending protocol while an
        // installer replaces files. Treat it as an explicit exit instead of
        // hiding the main window in the notification area.
        _exitRequested = true;
        base.OnSessionEnding(e);
    }

    public void Dispose()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _trayIconImage?.Dispose();
        _trayIconImage = null;
        if (_downloads is IDisposable disposable)
        {
            disposable.Dispose();
            _downloads = null;
        }
        if (_directSyncServer is not null)
        {
            _directSyncServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _directSyncServer = null;
        }
        _activationRegistration?.Unregister(null);
        _activationRegistration = null;
        _activationEvent?.Dispose();
        _activationEvent = null;
        if (_ownsMutex && _singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _ownsMutex = false;
        }
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        GC.SuppressFinalize(this);
    }

    private bool TryBecomePrimaryInstance(bool waitForPrevious)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: false, MutexName);
        try
        {
            _ownsMutex = _singleInstanceMutex.WaitOne(waitForPrevious ? TimeSpan.FromSeconds(30) : TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }

        if (!_ownsMutex)
        {
            SignalRunningInstance();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            return false;
        }

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => Dispatcher.BeginInvoke(ShowMainWindow),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
        return true;
    }

    private static void SignalRunningInstance()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var activation = EventWaitHandle.OpenExisting(ActivationEventName);
                activation.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException) when (attempt < 9)
            {
                Thread.Sleep(50);
            }
        }
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        var openItem = new Forms.ToolStripMenuItem(UiLanguage.T("Abrir UniShare"));
        openItem.Click += (_, _) => Dispatcher.BeginInvoke(ShowMainWindow);
        var startupItem = new Forms.ToolStripMenuItem(UiLanguage.T("Iniciar con Windows"))
        {
            Checked = StartupRegistration.IsEnabled(),
        };
        startupItem.Click += (_, _) => Dispatcher.BeginInvoke(() => ToggleStartup(startupItem));
        var exitItem = new Forms.ToolStripMenuItem(UiLanguage.T("Salir completamente"));
        exitItem.Click += (_, _) => Dispatcher.BeginInvoke(ExitApplication);
        menu.Items.Add(openItem);
        menu.Items.Add(startupItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _trayIconImage = Environment.ProcessPath is { } executable
            ? Icon.ExtractAssociatedIcon(executable)
            : null;
        _trayIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = _trayIconImage ?? SystemIcons.Application,
            Text = UiLanguage.T("UniShare - sincronización activa"),
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowMainWindow);
    }

    private static void ToggleStartup(Forms.ToolStripMenuItem item)
    {
        try
        {
            StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled());
            item.Checked = StartupRegistration.IsEnabled();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                UiLanguage.T($"No se pudo cambiar el inicio automático.\n\n{exception.Message}"),
                "UniShare",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_shutdownStarted)
        {
            return;
        }

        if (_exitRequested || _exitOnClose)
        {
            e.Cancel = true;
            _exitRequested = true;
            _ = ShutdownAsync();
            return;
        }

        e.Cancel = true;
        MainWindow?.Hide();
        if (!_closeTipShown && _trayIcon is not null)
        {
            _trayIcon.ShowBalloonTip(
                4000,
                UiLanguage.T("UniShare sigue sincronizando"),
                UiLanguage.T("La ventana se ocultó. Usa el icono junto al reloj para abrirla o salir completamente."),
                Forms.ToolTipIcon.Info);
            _closeTipShown = true;
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (!_shutdownStarted && (_exitRequested || _exitOnClose))
        {
            _ = ShutdownAsync();
        }
    }

    private async Task ShutdownAsync()
    {
        if (_shutdownStarted)
        {
            return;
        }

        _shutdownStarted = true;
        MainWindow?.Hide();
        var exitCode = 0;
        try
        {
            if (_directSyncServer is not null)
            {
                await _directSyncServer.DisposeAsync();
                _directSyncServer = null;
            }
        }
        catch
        {
            exitCode = 1;
        }
        finally
        {
            Shutdown(exitCode);
        }
    }

    private void ShowMainWindow()
    {
        if (MainWindow is null)
        {
            return;
        }
        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized)
        {
            MainWindow.WindowState = WindowState.Normal;
        }
        MainWindow.Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        if (MainWindow is not null)
        {
            MainWindow.Close();
        }
        else
        {
            _ = ShutdownAsync();
        }
    }

    private static string ResolveProfilePath(string[] arguments)
    {
        for (var index = 0; index < arguments.Length; index++)
        {
            if (string.Equals(arguments[index], "--profile", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index + 1]))
                {
                    throw new ArgumentException("--profile necesita una ruta.");
                }

                return Path.GetFullPath(arguments[index + 1]);
            }
        }

        var configuredPath = SelectedProfileConfigurationPath();
        if (File.Exists(configuredPath))
        {
            var selected = File.ReadAllText(configuredPath).Trim();
            if (!string.IsNullOrWhiteSpace(selected))
            {
                return Path.GetFullPath(selected);
            }
        }

        return DefaultProfilePath();
    }

    internal static void SaveSelectedProfilePath(string profileRoot)
    {
        var configurationPath = SelectedProfileConfigurationPath();
        Directory.CreateDirectory(Path.GetDirectoryName(configurationPath)!);
        var temporaryPath = configurationPath + ".partial";
        File.WriteAllText(temporaryPath, Path.GetFullPath(profileRoot));
        File.Move(temporaryPath, configurationPath, overwrite: true);
    }

    private static string DefaultProfilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UniShare");

    private static string SelectedProfileConfigurationPath() =>
        Path.Combine(DefaultProfilePath(), "config", "selected-profile.txt");
}
