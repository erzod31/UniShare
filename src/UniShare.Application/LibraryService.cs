using UniShare.Domain;

namespace UniShare.Application;

public sealed class LibraryService(IItemRepository repository, IBlobStore blobStore, TimeProvider timeProvider)
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        repository.InitializeAsync(cancellationToken);

    public async Task<LibraryItem> CreateAsync(
        CreateItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var hasUrl = !string.IsNullOrWhiteSpace(request.Url);
        var hasFile = !string.IsNullOrWhiteSpace(request.FilePath);

        if (!hasUrl && !hasFile)
        {
            throw new ArgumentException("Debes indicar un enlace, un archivo o ambos.", nameof(request));
        }

        if (hasFile && !File.Exists(request.FilePath))
        {
            throw new FileNotFoundException("El archivo seleccionado ya no existe.", request.FilePath);
        }

        var kind = (hasUrl, hasFile) switch
        {
            (true, true) => ItemKind.Hybrid,
            (true, false) => ItemKind.Link,
            (false, true) => ItemKind.File,
            _ => throw new InvalidOperationException(),
        };

        var now = timeProvider.GetUtcNow();
        var tags = request.Tags ?? [];
        var collections = request.Collections ?? [];
        foreach (var tag in tags)
        {
            _ = LibraryTag.Normalize(tag);
        }

        foreach (var collection in collections)
        {
            _ = LibraryCollectionDefinition.Normalize(collection);
        }

        var item = LibraryItem.Create(
            kind,
            request.Title,
            request.Url,
            now,
            source: request.Source,
            author: request.Author,
            description: request.Description,
            favorite: request.Favorite);

        AssetManifest? asset = null;
        if (hasFile)
        {
            var imported = await blobStore.ImportFileAsync(request.FilePath!, cancellationToken);
            asset = new AssetManifest(
                Guid.NewGuid(),
                item.Id,
                imported.Sha256,
                imported.ByteLength,
                imported.MimeType,
                imported.OriginalName,
                imported.RelativePath,
                now);
        }

        await repository.CreateAsync(item, asset, cancellationToken);
        if (tags.Count > 0)
        {
            await repository.ReplaceTagsAsync(item.Id, tags, now, cancellationToken);
        }

        if (collections.Count > 0)
        {
            await repository.ReplaceCollectionsAsync(item.Id, collections, now, cancellationToken);
        }

        return item;
    }

    public Task<IReadOnlyList<LibraryItem>> SearchAsync(
        string? query,
        bool includeDeleted = false,
        int limit = 200,
        CancellationToken cancellationToken = default) =>
        repository.SearchAsync(query, includeDeleted, limit, cancellationToken);

    public Task<IReadOnlyList<LibraryItem>> GetAllAsync(
        bool includeDeleted = false,
        CancellationToken cancellationToken = default) =>
        repository.GetAllAsync(includeDeleted, cancellationToken);

    public Task<IReadOnlyList<LibraryItem>> SearchAdvancedAsync(
        ItemSearchOptions options,
        CancellationToken cancellationToken = default) =>
        repository.SearchAdvancedAsync(options, cancellationToken);

    public Task<IReadOnlyList<CollectionSummary>> ListCollectionsAsync(
        CancellationToken cancellationToken = default) =>
        repository.ListCollectionsAsync(cancellationToken);

    public async Task<LibraryItem> UpdateAsync(
        Guid id,
        string title,
        string? source,
        string? author,
        string? description,
        bool favorite,
        CancellationToken cancellationToken = default)
    {
        var current = await repository.GetAsync(id, cancellationToken) ??
            throw new KeyNotFoundException("El elemento ya no existe.");
        var updated = current.UpdateMetadata(
            title, source, author, description, favorite, timeProvider.GetUtcNow());
        await repository.UpdateAsync(updated, cancellationToken);
        return updated;
    }

    public Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default) =>
        repository.SetDeletedAsync(id, true, timeProvider.GetUtcNow(), cancellationToken);

    public Task RestoreAsync(Guid id, CancellationToken cancellationToken = default) =>
        repository.SetDeletedAsync(id, false, timeProvider.GetUtcNow(), cancellationToken);

    public Task<IReadOnlyList<AssetManifest>> GetAssetsAsync(
        Guid itemId,
        CancellationToken cancellationToken = default) =>
        repository.GetAssetsAsync(itemId, cancellationToken);

    public Task<ItemOrganization> GetOrganizationAsync(
        Guid itemId,
        CancellationToken cancellationToken = default) =>
        repository.GetOrganizationAsync(itemId, cancellationToken);

    public async Task ReplaceOrganizationAsync(
        Guid itemId,
        IReadOnlyCollection<string> tags,
        IReadOnlyCollection<string> collections,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        await repository.ReplaceTagsAsync(itemId, tags, now, cancellationToken);
        await repository.ReplaceCollectionsAsync(itemId, collections, now, cancellationToken);
    }
}

