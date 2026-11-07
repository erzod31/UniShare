using UniShare.Domain;

namespace UniShare.Application;

public interface IItemRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task CreateAsync(
        LibraryItem item,
        AssetManifest? asset,
        CancellationToken cancellationToken = default);

    Task<LibraryItem?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LibraryItem>> GetAllAsync(
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LibraryItem>> SearchAsync(
        string? query,
        bool includeDeleted = false,
        int limit = 200,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LibraryItem>> SearchAdvancedAsync(
        ItemSearchOptions options,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CollectionSummary>> ListCollectionsAsync(
        CancellationToken cancellationToken = default);

    Task UpdateAsync(LibraryItem item, CancellationToken cancellationToken = default);

    Task SetDeletedAsync(Guid id, bool deleted, DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssetManifest>> GetAssetsAsync(Guid itemId, CancellationToken cancellationToken = default);

    Task<ItemOrganization> GetOrganizationAsync(Guid itemId, CancellationToken cancellationToken = default);

    Task ReplaceTagsAsync(
        Guid itemId,
        IReadOnlyCollection<string> names,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task ReplaceCollectionsAsync(
        Guid itemId,
        IReadOnlyCollection<string> names,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    async Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssetManifest>>> GetAssetsBatchAsync(
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, IReadOnlyList<AssetManifest>>();
        foreach (var id in itemIds.Distinct())
        {
            result[id] = await GetAssetsAsync(id, cancellationToken);
        }
        return result;
    }

    async Task<IReadOnlyDictionary<Guid, ItemOrganization>> GetOrganizationsBatchAsync(
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<Guid, ItemOrganization>();
        foreach (var id in itemIds.Distinct())
        {
            result[id] = await GetOrganizationAsync(id, cancellationToken);
        }
        return result;
    }

    Task<int> PurgeDeletedAsync(
        IReadOnlyCollection<Guid>? ids,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}

