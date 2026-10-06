using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using UniShare.Application;
using UniShare.Domain;

namespace UniShare.Desktop;

public sealed class MainWindowViewModel(LibraryService library) : INotifyPropertyChanged
{
    private const int PageSize = 200;
    private string _searchText = string.Empty;
    private bool _includeDeleted;
    private bool _offlineOnly;
    private bool _favoriteOnly;
    private int _filterIndex;
    private string? _collectionFilter;
    private ItemRow? _selectedItem;
    private string _statusMessage = UiLanguage.T("Preparado.");
    private bool _hasMore;
    private long _refreshGeneration;
    private int _loadMoreRunning;
    private int _selectionCount;
    private int _activeSelectionCount;
    private int _deletedSelectionCount;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ItemRow> Items { get; } = [];
    public ObservableCollection<CollectionSummary> Collections { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set => SetField(ref _searchText, value);
    }

    public bool IncludeDeleted
    {
        get => _includeDeleted;
        set => SetField(ref _includeDeleted, value);
    }

    public bool OfflineOnly
    {
        get => _offlineOnly;
        set => SetField(ref _offlineOnly, value);
    }

    public bool FavoriteOnly
    {
        get => _favoriteOnly;
        set => SetField(ref _favoriteOnly, value);
    }

    public int FilterIndex
    {
        get => _filterIndex;
        set => SetField(ref _filterIndex, value);
    }

    public string? CollectionFilter
    {
        get => _collectionFilter;
        set
        {
            if (string.Equals(_collectionFilter, value, StringComparison.Ordinal))
            {
                return;
            }
            _collectionFilter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ViewTitle));
        }
    }

    public string ViewTitle => UiLanguage.T(string.IsNullOrWhiteSpace(CollectionFilter)
        ? "Todos los elementos"
        : $"Carpeta: {CollectionFilter}");

    public ItemRow? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (EqualityComparer<ItemRow?>.Default.Equals(_selectedItem, value))
            {
                return;
            }

            _selectedItem = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionVisibility));
            OnPropertyChanged(nameof(NoSelectionVisibility));
            OnPropertyChanged(nameof(DeleteVisibility));
            OnPropertyChanged(nameof(RestoreVisibility));
            OnPropertyChanged(nameof(DeleteActionLabel));
            OnPropertyChanged(nameof(RestoreActionLabel));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, UiLanguage.T(value));
    }

    public Visibility EmptyVisibility => Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public string EmptyTitle => UiLanguage.T(!string.IsNullOrWhiteSpace(SearchText)
        ? "No hay resultados"
        : FavoriteOnly
            ? "No tienes favoritos"
            : OfflineOnly
                ? "No hay copias offline"
                : IncludeDeleted
                    ? "La papelera está vacía"
                    : "Tu biblioteca está vacía");

    public string EmptyDescription => UiLanguage.T(!string.IsNullOrWhiteSpace(SearchText)
        ? "Prueba con otros términos o borra la búsqueda."
        : FavoriteOnly
            ? "Marca un elemento como favorito desde Editar información."
            : OfflineOnly
                ? "Selecciona un enlace y guarda una copia offline."
                : IncludeDeleted
                    ? "Los elementos que elimines aparecerán aquí y podrás restaurarlos."
                    : "Guarda un enlace o arrastra archivos aquí.");

    public Visibility EmptyActionVisibility =>
        string.IsNullOrWhiteSpace(SearchText) && !FavoriteOnly && !OfflineOnly && !IncludeDeleted
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility SelectionVisibility => SelectedItem is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility NoSelectionVisibility => SelectedItem is null ? Visibility.Visible : Visibility.Collapsed;

    public Visibility MultiSelectionVisibility => _selectionCount > 1
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string SelectionSummary => UiLanguage.T(_selectionCount > 1
        ? $"{_selectionCount} elementos seleccionados. Eliminar y restaurar se aplicarán al grupo."
        : string.Empty);

    public string DeleteActionLabel => UiLanguage.T(EffectiveActiveSelectionCount > 1
        ? $"Eliminar ({EffectiveActiveSelectionCount})"
        : "Eliminar");

    public string RestoreActionLabel => UiLanguage.T(EffectiveDeletedSelectionCount > 1
        ? $"Restaurar ({EffectiveDeletedSelectionCount})"
        : "Restaurar");

    public Visibility DeleteVisibility => EffectiveActiveSelectionCount > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility RestoreVisibility => EffectiveDeletedSelectionCount > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility LoadMoreVisibility => _hasMore ? Visibility.Visible : Visibility.Collapsed;

    public void SetSelection(IReadOnlyCollection<ItemRow> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _selectionCount = items.Select(item => item.Id).Distinct().Count();
        _activeSelectionCount = items.Count(item => !item.IsDeleted);
        _deletedSelectionCount = items.Count(item => item.IsDeleted);
        OnPropertyChanged(nameof(MultiSelectionVisibility));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(DeleteVisibility));
        OnPropertyChanged(nameof(RestoreVisibility));
        OnPropertyChanged(nameof(DeleteActionLabel));
        OnPropertyChanged(nameof(RestoreActionLabel));
    }

    public async Task RefreshAsync()
    {
        var generation = Interlocked.Increment(ref _refreshGeneration);
        var selectedId = SelectedItem?.Id;
        var options = CreateSearchOptions(offset: 0);
        var collectionsTask = library.ListCollectionsAsync();
        var itemsTask = library.SearchAdvancedAsync(options);
        await Task.WhenAll(collectionsTask, itemsTask);

        if (generation != Volatile.Read(ref _refreshGeneration))
        {
            return;
        }

        var collectionSummaries = await collectionsTask;
        Collections.Clear();
        foreach (var collection in collectionSummaries)
        {
            Collections.Add(collection);
        }

        var items = await itemsTask;
        Items.Clear();
        foreach (var item in items.Take(PageSize))
        {
            Items.Add(new ItemRow(item));
        }
        _hasMore = items.Count > PageSize;
        CompleteRefresh(selectedId);
    }

    public async Task LoadMoreAsync()
    {
        if (!_hasMore || Interlocked.Exchange(ref _loadMoreRunning, 1) != 0)
        {
            return;
        }

        var generation = Volatile.Read(ref _refreshGeneration);
        var offset = Items.Count;
        var options = CreateSearchOptions(offset);
        try
        {
            var items = await library.SearchAdvancedAsync(options);
            if (generation != Volatile.Read(ref _refreshGeneration) || offset != Items.Count)
            {
                return;
            }

            foreach (var item in items.Take(PageSize))
            {
                Items.Add(new ItemRow(item));
            }
            _hasMore = items.Count > PageSize;
            CompleteRefresh(SelectedItem?.Id);
        }
        finally
        {
            Volatile.Write(ref _loadMoreRunning, 0);
        }
    }

    private ItemSearchOptions CreateSearchOptions(int offset) =>
        new(
            Query: SearchText,
            Kinds: FilterIndex switch
            {
                1 => new HashSet<ItemKind> { ItemKind.Link },
                2 => new HashSet<ItemKind> { ItemKind.File, ItemKind.Hybrid },
                3 => new HashSet<ItemKind> { ItemKind.Hybrid },
                _ => null,
            },
            IncludeDeleted: IncludeDeleted,
            DeletedOnly: IncludeDeleted,
            FavoriteOnly: FavoriteOnly,
            HasLocalAsset: OfflineOnly ? true : null,
            Collection: CollectionFilter,
            Limit: PageSize + 1,
            Offset: offset);

    private int EffectiveActiveSelectionCount => _selectionCount > 0
        ? _activeSelectionCount
        : SelectedItem is { IsDeleted: false } ? 1 : 0;

    private int EffectiveDeletedSelectionCount => _selectionCount > 0
        ? _deletedSelectionCount
        : SelectedItem is { IsDeleted: true } ? 1 : 0;

    private void CompleteRefresh(Guid? selectedId)
    {
        SelectedItem = Items.FirstOrDefault(value => value.Id == selectedId);
        StatusMessage = _hasMore
            ? $"{Items.Count} elementos cargados; hay más."
            : Items.Count == 1 ? "1 elemento." : $"{Items.Count} elementos.";
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
        OnPropertyChanged(nameof(EmptyActionVisibility));
        OnPropertyChanged(nameof(LoadMoreVisibility));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ItemRow(LibraryItem item)
{
    public LibraryItem Item { get; } = item;

    public Guid Id => Item.Id;

    public string Title => Item.Title;

    public string KindShort => Item.Kind switch
    {
        ItemKind.Link => "↗",
        ItemKind.File => "▤",
        ItemKind.Hybrid => "◆",
        _ => "?",
    };

    public string KindLabel => UiLanguage.T(Item.Kind switch
    {
        ItemKind.Link => "Enlace",
        ItemKind.File => "Archivo",
        ItemKind.Hybrid => "Enlace y archivo",
        _ => "Elemento",
    });

    public string Subtitle => Item.OriginalUrl ?? (UiLanguage.Choice == UiLanguageChoice.English
        ? $"Local file · {Item.UpdatedAt:dd/MM/yyyy HH:mm}"
        : $"Archivo local · {Item.UpdatedAt:dd/MM/yyyy HH:mm}");

    public string DescriptionPreview => Item.Description ?? string.Empty;

    public string Status => UiLanguage.T(Item.IsDeleted
        ? "En la papelera"
        : Item.Kind == ItemKind.Link ? "Sólo enlace" : "Disponible offline");

    public string UpdatedLabel => UiLanguage.Choice == UiLanguageChoice.English
        ? $"Updated {Item.UpdatedAt.ToLocalTime():dd/MM/yyyy · HH:mm}"
        : $"Actualizado {Item.UpdatedAt.ToLocalTime():dd/MM/yyyy · HH:mm}";

    public string DescriptionLabel => string.IsNullOrWhiteSpace(Item.Description)
        ? UiLanguage.T("Sin resumen disponible. Puedes añadir notas desde Editar información.")
        : Item.Description;

    public string FavoriteLabel => UiLanguage.T(Item.Favorite ? "★ Favorito" : "No marcado como favorito");

    public string AccessibilityLabel =>
        $"{Title}. {KindLabel}. {Status}. {Subtitle}. {DescriptionPreview}. {FavoriteLabel}.";

    public bool IsDeleted => Item.IsDeleted;
}

