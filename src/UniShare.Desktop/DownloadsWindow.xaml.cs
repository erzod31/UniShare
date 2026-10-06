using System.Globalization;
using System.Windows;
using UniShare.Application;
using UniShare.Domain;

namespace UniShare.Desktop;

public partial class DownloadsWindow : Window
{
    private readonly IDownloadService _downloads;

    public DownloadsWindow(IDownloadService downloads)
    {
        _downloads = downloads;
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (JobsGrid.SelectedItem is not DownloadRow row || !row.Job.CanRun)
        {
            StatusText.Text = UiLanguage.T("Selecciona una descarga fallida o pausada.");
            return;
        }

        await RunSafelyAsync(async () =>
        {
            StatusText.Text = UiLanguage.T("Descargando...");
            await _downloads.RunAsync(row.Job.Id);
            await RefreshAsync();
        });
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (JobsGrid.SelectedItem is not DownloadRow row)
        {
            StatusText.Text = UiLanguage.T("Selecciona una descarga.");
            return;
        }

        await RunSafelyAsync(async () =>
        {
            await _downloads.CancelAsync(row.Job.Id);
            await RefreshAsync();
        });
    }

    private async Task RefreshAsync()
    {
        var jobs = await _downloads.ListAsync();
        JobsGrid.ItemsSource = jobs.Select(job => new DownloadRow(job)).ToArray();
        StatusText.Text = UiLanguage.T(jobs.Count == 1 ? "1 descarga." : $"{jobs.Count} descargas.");
    }

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
    }

    private sealed class DownloadRow(DownloadJob job)
    {
        public DownloadJob Job { get; } = job;
        public string State => Job.State.ToString();
        public string Source => Job.SourceUri.AbsoluteUri;
        public string Progress => Job.TotalBytes.HasValue
            ? $"{Job.BytesReceived:N0} / {Job.TotalBytes:N0} bytes"
            : $"{Job.BytesReceived:N0} bytes";
        public string Updated => Job.UpdatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        public string Error => Job.ErrorMessage ?? string.Empty;
    }
}
