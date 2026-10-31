using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using UniShare.Application;
using UniShare.Infrastructure;

namespace UniShare.Desktop;

public partial class AppUpdatesWindow : Window, IDisposable
{
    private readonly HttpClient _http = GitHubAppUpdates.CreateHttpClient();
    private readonly GitHubAppUpdates _updates;
    private readonly string _current;
    private CancellationTokenSource? _operation;
    private AppUpdateCandidate? _candidate;
    private VerifiedAppUpdate? _ready;

    private static string T(string spanish, string english) => UiLanguage.Choice == UiLanguageChoice.English ? english : spanish;

    public AppUpdatesWindow(string profileRoot) : this(profileRoot, null) { }

    internal AppUpdatesWindow(string profileRoot, GitHubAppUpdates? updates)
    {
        InitializeComponent();
        Title = T("Actualizar UniShare", "Update UniShare");
        _current = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? throw new InvalidOperationException();
        using var channel = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "updates", "channel.json")));
        if (channel.RootElement.GetProperty("productId").GetString() != "unishare" ||
            channel.RootElement.GetProperty("schemaVersion").GetInt32() != 1 ||
            channel.RootElement.GetProperty("windowsAppId").GetString() != "BC1A70DC-8B2B-4B2B-8F2C-7A3EB751E3A7" ||
            channel.RootElement.GetProperty("channel").GetString() != "stable")
        {
            throw new InvalidDataException("Invalid application update channel.");
        }
        var repository = channel.RootElement.GetProperty("repository").GetString()!;
        GitHubAppUpdates.ValidateRepository(repository);
        _updates = updates ?? new(_http, repository, _current, Path.Combine(profileRoot, "cache", "app-updates"),
            WindowsUpdatePackageVerifier.ForCurrentApplication(),
            new WindowsUpdateInstaller(profileRoot, () => ((App)System.Windows.Application.Current).ShutdownForUpdateAsync()));
        VersionText.Text = T("Versión instalada: ", "Installed version: ") + _current;
        ChannelText.Text = "GitHub Releases · " + repository;
        StatusText.Text = T("Busca una versión nueva de la aplicación. Se conservan tus datos.",
            "Check for a newer application version. Your data is preserved.");
        CheckButton.Content = T("Buscar actualización", "Check for updates");
        DownloadButton.Content = T("Descargar", "Download");
        InstallButton.Content = T("Instalar y reabrir", "Install and reopen");
        CancelButton.Content = T("Cancelar", "Cancel");
    }

    private async void Check_Click(object sender, RoutedEventArgs e) => await RunAsync(async token =>
    {
        _ready = null;
        _candidate = null;
        DownloadProgress.Value = 0;
        var result = await _updates.CheckAsync(token);
        _candidate = result.Candidate;
        StatusText.Text = result.Candidate is { } next
            ? T("Nueva versión: ", "New version: ") + next.Version + $" · {next.Bytes / (1024.0 * 1024):F1} MB"
            : result.PublishedVersion is { } published
                ? T("Instalada: ", "Installed: ") + _current + T(". Publicada en el canal: ", ". Published on the channel: ") + published +
                    T(". No se ofrece una versión anterior.", ". An older version will not be offered.")
                : T("Todavía no hay una publicación estable en este canal.", "There is no stable release on this channel yet.");
    });

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_candidate is not { } candidate) { return; }
        await RunAsync(async token =>
        {
            var progress = new Progress<long>(received => DownloadProgress.Value = 100.0 * received / candidate.Bytes);
            _ready = await _updates.DownloadAsync(candidate, progress, token);
            StatusText.Text = T("Descarga verificada. ", "Download verified. ") +
                (_ready.SignatureStatus == "verified" ? T("Firma Authenticode válida.", "Valid Authenticode signature.") :
                    T("Paquete histórico sin firma Authenticode; origen y SHA-256 verificados.", "Legacy package without Authenticode; origin and SHA-256 verified."));
        });
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_ready is not { } ready) { return; }
        var accepted = MessageBox.Show(this,
            T("UniShare cerrará esta instancia y abrirá el asistente de actualización. El instalador conserva los datos y reabre la app al terminar. Si cancelas el asistente, abre UniShare manualmente.",
                "UniShare will close this instance and open the update wizard. The installer preserves data and reopens the app when finished. If you cancel the wizard, reopen UniShare manually.") +
            (ready.SignatureStatus == "legacy-unsigned" ? T("\n\nEste paquete no tiene firma Authenticode.", "\n\nThis package has no Authenticode signature.") : string.Empty) +
            T("\n\nSi usas el ZIP portátil, se instalará la edición por usuario conservando este perfil.", "\n\nIf using the portable ZIP, the per-user edition will be installed with this profile."),
            Title, MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.Cancel) == MessageBoxResult.OK;
        if (!accepted) { return; }
        await RunAsync(async token => { _ = await _updates.RequestInstallAsync(ready, true, token); });
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (_operation is not null) { return; }
        using var operation = new CancellationTokenSource();
        _operation = operation;
        CheckButton.IsEnabled = DownloadButton.IsEnabled = InstallButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        StatusText.Text = T("Comprobando o descargando…", "Checking or downloading…");
        try { await action(operation.Token); }
        catch (OperationCanceledException) { StatusText.Text = T("Operación cancelada. No se instaló nada.", "Operation canceled. Nothing was installed."); }
        catch (Exception exception) when (exception is IOException or HttpRequestException or InvalidOperationException or JsonException or System.Security.Cryptography.CryptographicException or System.ComponentModel.Win32Exception)
        {
            _ready = null;
            StatusText.Text = T("No se pudo verificar la actualización. Vuelve a intentarlo; no se instaló nada.",
                "The update could not be verified. Retry; nothing was installed.");
        }
        finally
        {
            _operation = null;
            CheckButton.IsEnabled = true;
            DownloadButton.IsEnabled = _candidate is not null && _ready is null;
            InstallButton.IsEnabled = _ready is not null;
            CancelButton.IsEnabled = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();
    private void Window_Closed(object? sender, EventArgs e) => Dispose();
    public void Dispose()
    {
        _operation?.Cancel();
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}
