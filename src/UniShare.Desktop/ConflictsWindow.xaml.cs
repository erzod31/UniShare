using System.Windows;
using UniShare.Application;

namespace UniShare.Desktop;

public partial class ConflictsWindow : Window
{
    private readonly ISyncService _sync;

    public ConflictsWindow(ISyncService sync)
    {
        _sync = sync;
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void KeepLocal_Click(object sender, RoutedEventArgs e) =>
        await ResolveAsync(SyncResolution.KeepLocal);

    private async void UseRemote_Click(object sender, RoutedEventArgs e) =>
        await ResolveAsync(SyncResolution.UseRemote);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async Task ResolveAsync(SyncResolution resolution)
    {
        if (ConflictsGrid.SelectedItem is not SyncConflict conflict)
        {
            StatusText.Text = UiLanguage.T("Selecciona primero un conflicto.");
            return;
        }

        try
        {
            await _sync.ResolveConflictAsync(conflict.Id, resolution);
            await RefreshAsync();
            StatusText.Text = UiLanguage.T(resolution == SyncResolution.KeepLocal
                ? "Se conservó tu versión."
                : "Se aplicó la versión recibida.");
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "UniShare", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task RefreshAsync()
    {
        var conflicts = await _sync.ListConflictsAsync();
        ConflictsGrid.ItemsSource = conflicts;
        StatusText.Text = UiLanguage.T(conflicts.Count == 0
            ? "No hay conflictos pendientes."
            : $"{conflicts.Count} conflicto(s) pendiente(s).");
    }
}
