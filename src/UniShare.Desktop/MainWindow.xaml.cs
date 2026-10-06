using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using UniShare.Application;
using UniShare.Infrastructure;

namespace UniShare.Desktop;

public partial class MainWindow : Window
{
    private const string OriginMetadataMigration = "3";
    private readonly MainWindowViewModel _viewModel;
    private readonly LibraryService _library;
    private readonly IBlobStore _blobStore;
    private readonly IBackupService _backup;
    private readonly ISyncService _sync;
    private readonly IObsidianVaultService _obsidian;
    private readonly IDownloadService _downloads;
    private readonly DirectSyncServer _directSyncServer;
    private readonly string? _directSyncError;
    private readonly string? _tailscaleSetupError;
    private readonly string _profileRoot;
    private bool _isCapturing;
    private bool _isRefreshingOriginMetadata;

    private static MessageBoxResult ShowMessage(
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image,
        MessageBoxResult defaultResult = MessageBoxResult.None) =>
        defaultResult == MessageBoxResult.None
            ? MessageBox.Show(UiLanguage.T(message), UiLanguage.T(caption), buttons, image)
            : MessageBox.Show(UiLanguage.T(message), UiLanguage.T(caption), buttons, image, defaultResult);

    public MainWindow(
        LibraryService library,
        IBlobStore blobStore,
        IBackupService backup,
        ISyncService sync,
        IObsidianVaultService obsidian,
        IDownloadService downloads,
        DirectSyncServer directSyncServer,
        string? directSyncError,
        string? tailscaleSetupError,
        string profileRoot)
    {
        _library = library;
        _blobStore = blobStore;
        _backup = backup;
        _sync = sync;
        _obsidian = obsidian;
        _downloads = downloads;
        _directSyncServer = directSyncServer;
        _directSyncError = directSyncError;
        _tailscaleSetupError = tailscaleSetupError;
        _profileRoot = Path.GetFullPath(profileRoot);
        _viewModel = new MainWindowViewModel(library);
        InitializeComponent();
        DataContext = _viewModel;
        ThemeMenuItem.Header = UiLanguage.Choice == UiLanguageChoice.English
            ? $"Theme: {DesktopTheme.Label}"
            : $"Tema: {DesktopTheme.Label}";
        TextScaleMenuItem.Header = UiLanguage.Choice == UiLanguageChoice.English
            ? $"Text size: {DesktopTheme.TextScaleLabel}"
            : $"Tamaño del texto: {DesktopTheme.TextScaleLabel}";
        LanguageMenuItem.Header = UiLanguage.Choice == UiLanguageChoice.English
            ? "Language: English"
            : "Idioma: Español";
        SyncStatusText.Text = _directSyncError is not null
            ? "Requiere revisión"
            : !_directSyncServer.IsRunning
                ? "Sin conexión"
                : _tailscaleSetupError is null
                    ? "Activa en segundo plano"
                    : "Nodo local activo";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyResponsiveLayout(ActualWidth);
        await RefreshSafelyAsync();
        _ = RefreshOriginMetadataAsync(automaticOnly: true);
    }

    private async void RefreshOriginMetadata_Click(object sender, RoutedEventArgs e) =>
        await RefreshOriginMetadataAsync(automaticOnly: false);

    private async Task RefreshOriginMetadataAsync(bool automaticOnly)
    {
        if (_isRefreshingOriginMetadata)
        {
            if (!automaticOnly)
            {
                _viewModel.StatusMessage = "Ya se están actualizando los títulos y los resúmenes.";
            }
            return;
        }

        _isRefreshingOriginMetadata = true;
        try
        {
            var migrationMarker = Path.Combine(
                _profileRoot, "config", $"origin-metadata-{OriginMetadataMigration}.completed");
            if (automaticOnly && File.Exists(migrationMarker))
            {
                return;
            }

            var allItems = await _library.GetAllAsync();
            var candidates = allItems
                .Where(item => item.OriginalUrl is not null)
                .ToArray();
            if (!automaticOnly)
            {
                _viewModel.StatusMessage = candidates.Length == 0
                    ? "No hay enlaces que actualizar."
                    : $"Analizando el contenido de {candidates.Length} enlaces…";
            }

            var updated = 0;
            var unavailable = 0;
            foreach (var item in candidates)
            {
                try
                {
                    var metadata = await _downloads.FetchMetadataAsync(new Uri(item.OriginalUrl!));
                    if (metadata is null)
                    {
                        continue;
                    }

                    var title = IsAutomaticTitle(item) && !string.IsNullOrWhiteSpace(metadata.Title)
                        ? metadata.Title
                        : item.Title;
                    var source = string.IsNullOrWhiteSpace(item.Source) ? metadata.Source : item.Source;
                    var author = string.IsNullOrWhiteSpace(item.Author) ? metadata.Author : item.Author;
                    var description = metadata.ResolveDescription(item.Description);
                    if (title == item.Title && source == item.Source && author == item.Author &&
                        description == item.Description)
                    {
                        continue;
                    }

                    await _library.UpdateAsync(
                        item.Id, title!, source, author, description, item.Favorite);
                    updated++;
                }
                catch
                {
                    unavailable++;
                }
            }

            if (updated > 0)
            {
                await _viewModel.RefreshAsync();
                _viewModel.StatusMessage = $"Títulos y resúmenes actualizados en {updated} enlaces." +
                    (unavailable > 0 ? $" {unavailable} sitios no respondieron." : string.Empty);
            }
            else if (!automaticOnly && candidates.Length > 0)
            {
                _viewModel.StatusMessage = unavailable > 0
                    ? $"No se pudo consultar la información de {unavailable} sitios."
                    : "Los títulos y los resúmenes ya estaban actualizados.";
            }

            if (automaticOnly && (candidates.Length == 0 || unavailable < candidates.Length))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(migrationMarker)!);
                await File.WriteAllTextAsync(migrationMarker, DateTimeOffset.UtcNow.ToString("O"));
            }
        }
        catch (Exception exception)
        {
            if (!automaticOnly)
            {
                _viewModel.StatusMessage = $"No se pudieron actualizar los resúmenes: {exception.Message}";
            }
        }
        finally
        {
            _isRefreshingOriginMetadata = false;
        }
    }

    private static bool IsAutomaticTitle(UniShare.Domain.LibraryItem item) =>
        item.OriginalUrl is not null &&
        string.Equals(
            item.Title,
            LinkCaptureDefaults.TitleFromUrl(new Uri(item.OriginalUrl)),
            StringComparison.Ordinal);

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        var compact = width < 1080;
        SidebarColumn.Width = new GridLength(compact ? 190 : 220);
        DetailsColumn.Width = compact ? new GridLength(0) : new GridLength(0.85, GridUnitType.Star);
        DetailsPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        ShortcutHint.Visibility = width < 980 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        DesktopTheme.CycleAndApply(System.Windows.Application.Current);
        ThemeMenuItem.Header = UiLanguage.Choice == UiLanguageChoice.English
            ? $"Theme: {DesktopTheme.Label}"
            : $"Tema: {DesktopTheme.Label}";
        _viewModel.StatusMessage = UiLanguage.Choice == UiLanguageChoice.English
            ? $"{DesktopTheme.Label} theme enabled."
            : $"Tema {DesktopTheme.Label.ToLowerInvariant()} activado.";
    }

    private void TextScale_Click(object sender, RoutedEventArgs e)
    {
        DesktopTheme.CycleTextScaleAndApply(System.Windows.Application.Current);
        TextScaleMenuItem.Header = UiLanguage.Choice == UiLanguageChoice.English
            ? $"Text size: {DesktopTheme.TextScaleLabel}"
            : $"Tamaño del texto: {DesktopTheme.TextScaleLabel}";
        _viewModel.StatusMessage = UiLanguage.Choice == UiLanguageChoice.English
            ? $"{DesktopTheme.TextScaleLabel} text size enabled."
            : $"Tamaño del texto {DesktopTheme.TextScaleLabel.ToLowerInvariant()} activado.";
    }

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        UiLanguage.ToggleAndSave();
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("No se encontró el ejecutable.");
        Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Arguments = "--wait-for-previous",
        });
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && QuickCapturePanel.Visibility == Visibility.Visible)
        {
            QuickCapturePanel.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F6)
        {
            if (QuickCapturePanel.Visibility == Visibility.Visible && !QuickUrlBox.IsKeyboardFocusWithin)
            {
                QuickUrlBox.Focus();
            }
            else if (!LibraryItemsList.IsKeyboardFocusWithin)
            {
                LibraryItemsList.Focus();
            }
            else
            {
                SearchBox.Focus();
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && GetSelectedRows().Any(item => !item.IsDeleted))
        {
            Delete_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        if (e.Key == Key.L)
        {
            ShowQuickCapture();
            QuickUrlBox.Focus();
            QuickUrlBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.N)
        {
            NewItem_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.E && _viewModel.SelectedItem is not null)
        {
            Edit_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void LibraryItemsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel.SelectedItem is not null)
        {
            Open_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void LibraryItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _viewModel.SetSelection(LibraryItemsList.SelectedItems.Cast<ItemRow>().ToArray());

    private void LibraryItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedItem is not null)
        {
            Open_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void ToggleQuickCapture_Click(object sender, RoutedEventArgs e)
    {
        if (QuickCapturePanel.Visibility == Visibility.Visible)
        {
            QuickCapturePanel.Visibility = Visibility.Collapsed;
            return;
        }

        ShowQuickCapture();
    }

    private void ShowQuickCapture()
    {
        QuickCapturePanel.Visibility = Visibility.Visible;
        QuickUrlBox.Focus();
    }

    private async void AllItems_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CollectionFilter = null;
        _viewModel.FavoriteOnly = false;
        _viewModel.OfflineOnly = false;
        _viewModel.IncludeDeleted = false;
        SetActiveNavigation(LibraryNavButton);
        await RefreshSafelyAsync();
    }

    private async void FavoritesNav_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CollectionFilter = null;
        _viewModel.FavoriteOnly = true;
        _viewModel.OfflineOnly = false;
        _viewModel.IncludeDeleted = false;
        SetActiveNavigation(FavoritesNavButton);
        await RefreshSafelyAsync();
    }

    private async void OfflineNav_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CollectionFilter = null;
        _viewModel.FavoriteOnly = false;
        _viewModel.OfflineOnly = true;
        _viewModel.IncludeDeleted = false;
        SetActiveNavigation(OfflineNavButton);
        await RefreshSafelyAsync();
    }

    private async void ArchivedNav_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CollectionFilter = null;
        _viewModel.FavoriteOnly = false;
        _viewModel.OfflineOnly = false;
        _viewModel.IncludeDeleted = true;
        SetActiveNavigation(ArchivedNavButton);
        await RefreshSafelyAsync();
    }

    private async void CollectionNav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string collection })
        {
            return;
        }
        _viewModel.CollectionFilter = collection;
        _viewModel.FavoriteOnly = false;
        _viewModel.OfflineOnly = false;
        _viewModel.IncludeDeleted = false;
        SetActiveNavigation(null);
        await RefreshSafelyAsync();
    }

    private void SetActiveNavigation(System.Windows.Controls.Button? active)
    {
        foreach (var button in new[] { LibraryNavButton, FavoritesNavButton, OfflineNavButton, ArchivedNavButton })
        {
            button.Style = (Style)FindResource(button == active
                ? "ActiveNavigationButton"
                : "NavigationButton");
        }
    }

    private async void SelectLibrary_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = UiLanguage.T("Elige la carpeta raíz de tu biblioteca UniShare"),
            InitialDirectory = Directory.Exists(_profileRoot) ? _profileRoot : null,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var selectedPath = Path.GetFullPath(dialog.FolderName);
        if (string.Equals(selectedPath, _profileRoot, StringComparison.OrdinalIgnoreCase))
        {
            _viewModel.StatusMessage = "Esta biblioteca ya está abierta.";
            return;
        }

        try
        {
            await LibraryProfileValidator.ValidateExistingAsync(selectedPath);
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or UnauthorizedAccessException)
        {
            ShowMessage(
                exception.Message,
                "Biblioteca no válida",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (Directory.EnumerateFileSystemEntries(selectedPath).Any() &&
            !File.Exists(Path.Combine(selectedPath, "library.db")))
        {
            var decision = ShowMessage(
                "La carpeta no está vacía y todavía no contiene una biblioteca UniShare. " +
                "Se añadirán library.db y las carpetas privadas de contenido. ¿Quieres continuar?",
                "Elegir biblioteca",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (decision != MessageBoxResult.Yes)
            {
                return;
            }
        }

        App.SaveSelectedProfilePath(selectedPath);
        var executable = Environment.ProcessPath ??
            throw new InvalidOperationException("No se pudo determinar el ejecutable actual.");
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = true };
        startInfo.ArgumentList.Add("--profile");
        startInfo.ArgumentList.Add(selectedPath);
        startInfo.ArgumentList.Add("--wait-for-previous");
        Process.Start(startInfo);
        System.Windows.Application.Current.Shutdown();
    }

    private async void QuickSave_Click(object sender, RoutedEventArgs e) =>
        await CaptureUrlAsync(QuickOfflineCheck.IsChecked == true);

    private async void QuickUrlBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await CaptureUrlAsync(QuickOfflineCheck.IsChecked == true);
        }
    }

    private async Task CaptureUrlAsync(bool downloadOffline)
    {
        if (_isCapturing)
        {
            return;
        }

        var rawUrl = QuickUrlBox.Text.Trim();
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            ShowMessage(
                "Pega un enlace completo que empiece por http:// o https://.",
                "UniShare",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            QuickUrlBox.Focus();
            return;
        }

        _isCapturing = true;
        QuickSaveButton.IsEnabled = false;
        QuickSaveButton.Content = "Guardando…";
        try
        {
            await RunSafelyAsync(async () =>
            {
                var collections = SplitNames(QuickCollectionBox.Text);
                _viewModel.StatusMessage = downloadOffline
                    ? "Guardando enlace y preparando la copia offline..."
                    : "Guardando enlace...";
                var item = await _library.CreateAsync(new CreateItemRequest(
                    LinkCaptureDefaults.TitleFromUrl(uri),
                    uri.AbsoluteUri,
                    null,
                    Collections: collections));

                Exception? downloadError = null;
                Exception? metadataError = null;
                if (downloadOffline)
                {
                    try
                    {
                        var job = await _downloads.QueueAsync(item.Id, uri);
                        await _downloads.RunAsync(job.Id);
                    }
                    catch (Exception exception)
                    {
                        downloadError = exception;
                    }
                }
                else
                {
                    try
                    {
                        var metadata = await _downloads.FetchMetadataAsync(uri);
                        if (metadata is not null)
                        {
                            item = await _library.UpdateAsync(
                                item.Id,
                                metadata.Title ?? item.Title,
                                metadata.Source,
                                metadata.Author,
                                metadata.Description,
                                item.Favorite);
                        }
                    }
                    catch (Exception exception)
                    {
                        metadataError = exception;
                    }
                }

                QuickUrlBox.Clear();
                await _viewModel.RefreshAsync();
                _viewModel.SelectedItem = _viewModel.Items.FirstOrDefault(row => row.Id == item.Id);
                _viewModel.StatusMessage = downloadError is not null
                    ? "Enlace guardado; la copia offline quedó pendiente. Reinténtala desde Opciones > Descargas."
                    : metadataError is not null
                        ? "Enlace guardado con título provisional; no se pudieron consultar sus metadatos."
                    : downloadOffline
                        ? "Enlace, información disponible y copia offline guardados. Puedes editar el resultado."
                        : "Enlace guardado. Puedes editar su información cuando quieras.";
                QuickCapturePanel.Visibility = Visibility.Collapsed;

                if (downloadError is not null)
                {
                    ShowMessage(
                        $"El enlace se guardó correctamente, pero la copia offline no pudo completarse. " +
                        $"Puedes reintentarlo desde Opciones > Ver descargas pendientes.\n\n" +
                        $"Detalle: {downloadError.Message}",
                        "Copia offline pendiente",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            });
        }
        finally
        {
            _isCapturing = false;
            QuickSaveButton.IsEnabled = true;
            QuickSaveButton.Content = "Guardar";
        }
    }

    private async void NewItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ItemEditorWindow { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            await _library.CreateAsync(dialog.Request!);
            await _viewModel.RefreshAsync();
            _viewModel.StatusMessage = "Elemento guardado localmente.";
        });
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await RefreshSafelyAsync();

    private async void LoadMore_Click(object sender, RoutedEventArgs e) =>
        await RunSafelyAsync(_viewModel.LoadMoreAsync);

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".zip",
            FileName = $"UniShare-{DateTime.Now:yyyyMMdd-HHmmss}.unishare.zip",
            Filter = UiLanguage.T("Respaldo de UniShare (*.unishare.zip)|*.unishare.zip|Archivo ZIP (*.zip)|*.zip"),
            OverwritePrompt = true,
            Title = UiLanguage.T("Exportar respaldo de UniShare"),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            var summary = await _backup.ExportAsync(dialog.FileName);
            _viewModel.StatusMessage = $"Respaldo verificado: {summary.ItemCount} elementos y {summary.BlobCount} archivos.";
        });
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        var backupDialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = UiLanguage.T("Respaldo de UniShare (*.unishare.zip)|*.unishare.zip|Archivo ZIP (*.zip)|*.zip"),
            Multiselect = false,
            Title = UiLanguage.T("Selecciona la copia de seguridad de UniShare"),
        };
        if (backupDialog.ShowDialog(this) != true)
        {
            return;
        }

        var destinationDialog = new OpenFolderDialog
        {
            Title = UiLanguage.T("Elige dónde crear la biblioteca restaurada"),
            Multiselect = false,
        };
        if (destinationDialog.ShowDialog(this) != true)
        {
            return;
        }

        var destination = Path.Combine(
            destinationDialog.FolderName,
            $"UniShare-restaurada-{DateTime.Now:yyyyMMdd-HHmmss}");
        var decision = ShowMessage(
            $"La copia se verificará y se restaurará sin modificar la biblioteca actual.\n\n" +
            $"Nueva biblioteca:\n{destination}\n\n¿Continuar?",
            "Restaurar copia",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            _viewModel.StatusMessage = "Verificando y restaurando la copia...";
            var summary = await _backup.RestoreToNewProfileAsync(backupDialog.FileName, destination);
            _viewModel.StatusMessage =
                $"Copia restaurada: {summary.ItemCount} elementos y {summary.BlobCount} archivos.";
            var open = ShowMessage(
                $"La copia se restauró correctamente en:\n{destination}\n\n" +
                "¿Quieres abrir ahora esa biblioteca?",
                "Restauración completada",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (open == MessageBoxResult.Yes)
            {
                OpenProfileAndCloseCurrent(destination);
            }
        });
    }

    private async void ExportObsidian_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = UiLanguage.T("Elige la carpeta raíz de tu bóveda de Obsidian"),
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            var result = await _obsidian.ExportAsync(dialog.FolderName);
            _viewModel.StatusMessage =
                $"Obsidian actualizado: {result.NoteCount} notas y {result.AssetCount} archivos; " +
                $"{result.PreservedNoteCount} notas editables conservadas.";
        });
    }

    private async void ImportObsidian_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = UiLanguage.T("Elige la bóveda de Obsidian exportada por UniShare"),
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var decision = ShowMessage(
            "Se importará únicamente el bloque “Notas editables” de cada nota. " +
            "Los títulos, enlaces, etiquetas y archivos no se modificarán. ¿Continuar?",
            "Importar cambios de Obsidian",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (decision != MessageBoxResult.Yes)
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            var result = await _obsidian.ImportNotesAsync(dialog.FolderName);
            await _viewModel.RefreshAsync();
            _viewModel.StatusMessage =
                $"Obsidian importado: {result.UpdatedItemCount} cambios, " +
                $"{result.UnchangedItemCount} sin cambios y {result.IgnoredFileCount} ignorados.";
        });
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasSupportedDrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var files = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? e.Data.GetData(DataFormats.FileDrop) as string[]
            : null;
        var text = files is null || files.Length == 0
            ? ReadDroppedText(e.Data)
            : null;
        var plan = BatchImportPlanner.Create(files, text, SplitNames(QuickCollectionBox.Text));
        if (plan.Requests.Count == 0)
        {
            ShowMessage(
                "No se encontraron archivos ni enlaces http/https válidos.",
                "Importar elementos",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var imported = 0;
        var errors = new List<string>(plan.Rejected.Select(value => $"Entrada no válida: {value}"));
        _viewModel.StatusMessage = $"Importando {plan.Requests.Count} elementos...";
        foreach (var request in plan.Requests)
        {
            try
            {
                await _library.CreateAsync(request);
                imported++;
            }
            catch (Exception exception)
            {
                errors.Add($"{request.FilePath ?? request.Url}: {exception.Message}");
            }
        }

        await RefreshSafelyAsync();
        _viewModel.StatusMessage = errors.Count == 0
            ? $"{imported} elementos importados correctamente."
            : $"{imported} elementos importados; {errors.Count} entradas no pudieron guardarse.";
        if (errors.Count > 0)
        {
            ShowMessage(
                string.Join(Environment.NewLine, errors.Take(12)) +
                (errors.Count > 12 ? $"\n… y {errors.Count - 12} errores más." : string.Empty),
                "Resultado de la importación",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static bool HasSupportedDrop(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) ||
        data.GetDataPresent(DataFormats.UnicodeText) ||
        data.GetDataPresent(DataFormats.Text);

    private static string? ReadDroppedText(IDataObject data) =>
        data.GetData(DataFormats.UnicodeText) as string ?? data.GetData(DataFormats.Text) as string;

    private static void OpenProfileAndCloseCurrent(string profileRoot)
    {
        App.SaveSelectedProfilePath(profileRoot);
        var executable = Environment.ProcessPath ??
            throw new InvalidOperationException("No se pudo determinar el ejecutable actual.");
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = true };
        startInfo.ArgumentList.Add("--profile");
        startInfo.ArgumentList.Add(profileRoot);
        startInfo.ArgumentList.Add("--wait-for-previous");
        Process.Start(startInfo);
        System.Windows.Application.Current.Shutdown();
    }

    private void ConfigureDirectSync_Click(object sender, RoutedEventArgs e) =>
        new DirectSyncWindow(
            _directSyncServer, _directSyncServer.IsRunning, _directSyncError, _tailscaleSetupError)
        {
            Owner = this,
        }.ShowDialog();

    private void ShowConflicts_Click(object sender, RoutedEventArgs e)
    {
        var window = new ConflictsWindow(_sync) { Owner = this };
        window.ShowDialog();
        _ = RefreshSafelyAsync();
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSingleSelectedRow("guardar una copia offline");
        if (selected?.Item.OriginalUrl is null)
        {
            _viewModel.StatusMessage = "Selecciona un enlace para descargar.";
            return;
        }

        await RunSafelyAsync(async () =>
        {
            _viewModel.StatusMessage = "Descargando y verificando contenido...";
            var job = await _downloads.QueueAsync(selected.Id, new Uri(selected.Item.OriginalUrl));
            var completed = await _downloads.RunAsync(job.Id);
            await _viewModel.RefreshAsync();
            _viewModel.StatusMessage = $"Descarga verificada: {completed.BytesReceived:N0} bytes disponibles offline.";
        });
    }

    private void ShowDownloads_Click(object sender, RoutedEventArgs e)
    {
        var window = new DownloadsWindow(_downloads) { Owner = this };
        window.ShowDialog();
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await RefreshSafelyAsync();
        }
    }

    private async void FilterChanged(object sender, RoutedEventArgs e) => await RefreshSafelyAsync();

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows().Where(item => !item.IsDeleted).ToArray();
        if (selected.Length == 0)
        {
            _viewModel.StatusMessage = "Selecciona uno o más elementos de la biblioteca.";
            return;
        }

        var prompt = selected.Length == 1
            ? $"¿Eliminar «{selected[0].Title}»?\n\nSe moverá a la Papelera y el cambio se sincronizará. Podrás restaurarlo después."
            : $"¿Eliminar {selected.Length} elementos?\n\nSe moverán a la Papelera y los cambios se sincronizarán. Podrás restaurarlos después.";
        var answer = ShowMessage(
            prompt,
            selected.Length == 1 ? "Eliminar elemento" : "Eliminar elementos",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            foreach (var item in selected)
            {
                await _library.ArchiveAsync(item.Id);
            }
            await _viewModel.RefreshAsync();
            _viewModel.StatusMessage = selected.Length == 1
                ? "Elemento eliminado. Puedes restaurarlo desde la Papelera."
                : $"{selected.Length} elementos eliminados. Puedes restaurarlos desde la Papelera.";
        });
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows().Where(item => item.IsDeleted).ToArray();
        if (selected.Length == 0)
        {
            _viewModel.StatusMessage = "Selecciona uno o más elementos de la Papelera.";
            return;
        }

        await RunSafelyAsync(async () =>
        {
            foreach (var item in selected)
            {
                await _library.RestoreAsync(item.Id);
            }
            await _viewModel.RefreshAsync();
            _viewModel.StatusMessage = selected.Length == 1
                ? "Elemento restaurado en la biblioteca."
                : $"{selected.Length} elementos restaurados en la biblioteca.";
        });
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSingleSelectedRow("editar");
        if (selected is null)
        {
            _viewModel.StatusMessage = "Selecciona un elemento.";
            return;
        }

        ItemOrganization organization;
        try
        {
            organization = await _library.GetOrganizationAsync(selected.Id);
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, "UniShare", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new MetadataEditorWindow(selected.Item, organization) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await RunSafelyAsync(async () =>
        {
            await _library.UpdateAsync(
                selected.Id,
                dialog.ItemTitle,
                dialog.ItemSource,
                dialog.ItemAuthor,
                dialog.ItemDescription,
                dialog.ItemFavorite);
            await _library.ReplaceOrganizationAsync(
                selected.Id,
                dialog.ItemTags,
                dialog.ItemCollections);
            await _viewModel.RefreshAsync();
            _viewModel.StatusMessage = "Cambios guardados.";
        });
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSingleSelectedRow("abrir");
        if (selected is null)
        {
            _viewModel.StatusMessage = "Selecciona un elemento.";
            return;
        }

        await RunSafelyAsync(async () =>
        {
            var assets = await _library.GetAssetsAsync(selected.Id);
            var target = assets.Count > 0
                ? _blobStore.GetAbsolutePath(assets[0].RelativePath)
                : selected.Item.OriginalUrl;
            if (string.IsNullOrWhiteSpace(target))
            {
                throw new InvalidOperationException("Este elemento no tiene contenido disponible para abrir.");
            }

            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            _viewModel.StatusMessage = "Contenido abierto con la aplicación predeterminada.";
        });
    }

    private Task RefreshSafelyAsync() => RunSafelyAsync(_viewModel.RefreshAsync);

    private ItemRow[] GetSelectedRows()
    {
        var selected = LibraryItemsList.SelectedItems.Cast<ItemRow>().ToArray();
        if (selected.Length == 0 && _viewModel.SelectedItem is { } current)
        {
            return [current];
        }

        return selected;
    }

    private ItemRow? GetSingleSelectedRow(string action)
    {
        var selected = GetSelectedRows();
        if (selected.Length > 1)
        {
            _viewModel.StatusMessage = $"Selecciona un solo elemento para {action}.";
            return null;
        }

        return selected.SingleOrDefault();
    }

    private static string[] SplitNames(string value) =>
        value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            ShowMessage(exception.Message, "UniShare", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
