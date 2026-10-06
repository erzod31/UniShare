using UniShare.Application;
using UniShare.Domain;

namespace UniShare.Desktop.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void ItemRowExposesOriginDescriptionInTheLibraryCard()
    {
        var item = LibraryItem.Create(
            ItemKind.Link,
            "Título de origen",
            "https://example.com/article",
            new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero),
            description: "Descripción publicada por la página");

        var row = new ItemRow(item);

        Assert.Equal("Descripción publicada por la página", row.DescriptionPreview);
        Assert.Contains("Descripción publicada por la página", row.AccessibilityLabel);
    }

    [Fact]
    public void MultipleSelectionExposesBatchDeleteAndRestoreActions()
    {
        var viewModel = CreateViewModel(new ControlledRepository());
        var active = new ItemRow(CreateItem("Activo", 1));
        var deleted = new ItemRow(CreateItem("Papelera", 2).Archive(DateTimeOffset.UtcNow));
        var secondDeleted = new ItemRow(CreateItem("Papelera 2", 3).Archive(DateTimeOffset.UtcNow));

        viewModel.SelectedItem = active;
        viewModel.SetSelection([active, deleted, secondDeleted]);

        Assert.Equal(System.Windows.Visibility.Visible, viewModel.MultiSelectionVisibility);
        Assert.Equal(System.Windows.Visibility.Visible, viewModel.DeleteVisibility);
        Assert.Equal(System.Windows.Visibility.Visible, viewModel.RestoreVisibility);
        Assert.Equal("Eliminar", viewModel.DeleteActionLabel);
        Assert.Equal("Restaurar (2)", viewModel.RestoreActionLabel);
        Assert.Contains("3 elementos seleccionados", viewModel.SelectionSummary);
    }

    [Fact]
    public async Task RefreshAsyncDoesNotPublishAnOlderRequestAfterANewerSearch()
    {
        var repository = new ControlledRepository();
        var viewModel = CreateViewModel(repository);

        viewModel.SearchText = "primera";
        var firstRefresh = viewModel.RefreshAsync();
        viewModel.SearchText = "segunda";
        var secondRefresh = viewModel.RefreshAsync();

        repository.CompleteSearch("segunda", [CreateItem("Resultado nuevo", 2)]);
        await secondRefresh;
        repository.CompleteSearch("primera", [CreateItem("Resultado antiguo", 1)]);
        await firstRefresh;

        var item = Assert.Single(viewModel.Items);
        Assert.Equal("Resultado nuevo", item.Title);
    }

    [Fact]
    public async Task LoadMoreAsyncCoalescesConcurrentRequests()
    {
        var repository = new ControlledRepository();
        var viewModel = CreateViewModel(repository);
        var firstPage = Enumerable.Range(0, 201)
            .Select(index => CreateItem($"Elemento {index}", index))
            .ToArray();

        var refresh = viewModel.RefreshAsync();
        repository.CompleteSearch(string.Empty, firstPage);
        await refresh;

        var firstLoad = viewModel.LoadMoreAsync();
        var duplicateLoad = viewModel.LoadMoreAsync();
        repository.CompleteSearch(string.Empty, [CreateItem("Elemento 200", 200)], offset: 200);
        await Task.WhenAll(firstLoad, duplicateLoad);

        Assert.Equal(201, viewModel.Items.Count);
        Assert.Equal(2, repository.SearchCallCount);
    }

    private static MainWindowViewModel CreateViewModel(ControlledRepository repository) =>
        new(new LibraryService(repository, new UnusedBlobStore(), TimeProvider.System));

    private static LibraryItem CreateItem(string title, int index) =>
        LibraryItem.Create(
            ItemKind.Link,
            title,
            $"https://example.com/{index}",
            new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));

    private sealed class ControlledRepository : IItemRepository
    {
        private readonly object _gate = new();
        private readonly List<PendingSearch> _pending = [];
        private int _searchCallCount;

        public int SearchCallCount => Volatile.Read(ref _searchCallCount);

        public Task<IReadOnlyList<LibraryItem>> SearchAdvancedAsync(
            ItemSearchOptions options,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _searchCallCount);
            var completion = new TaskCompletionSource<IReadOnlyList<LibraryItem>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                _pending.Add(new PendingSearch(options.Query ?? string.Empty, options.Offset, completion));
            }
            return completion.Task;
        }

        public void CompleteSearch(string query, IReadOnlyList<LibraryItem> items, int offset = 0)
        {
            TaskCompletionSource<IReadOnlyList<LibraryItem>> completion;
            lock (_gate)
            {
                var pending = _pending.First(value => value.Query == query && value.Offset == offset);
                _pending.Remove(pending);
                completion = pending.Completion;
            }
            completion.SetResult(items);
        }

        public Task<IReadOnlyList<CollectionSummary>> ListCollectionsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CollectionSummary>>([]);

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CreateAsync(LibraryItem item, AssetManifest? asset, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LibraryItem?> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryItem>> GetAllAsync(bool includeDeleted = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryItem>> SearchAsync(string? query, bool includeDeleted = false, int limit = 200, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(LibraryItem item, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetDeletedAsync(Guid id, bool deleted, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AssetManifest>> GetAssetsAsync(Guid itemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ItemOrganization> GetOrganizationAsync(Guid itemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReplaceTagsAsync(Guid itemId, IReadOnlyCollection<string> names, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReplaceCollectionsAsync(Guid itemId, IReadOnlyCollection<string> names, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private sealed record PendingSearch(
            string Query,
            int Offset,
            TaskCompletionSource<IReadOnlyList<LibraryItem>> Completion);
    }

    private sealed class UnusedBlobStore : IBlobStore
    {
        public Task<BlobImportResult> ImportFileAsync(string sourcePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> VerifyAsync(string sha256, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public string GetAbsolutePath(string relativePath) => throw new NotSupportedException();
    }
}
