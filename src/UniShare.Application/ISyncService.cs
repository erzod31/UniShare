namespace UniShare.Application;

public interface ISyncService
{
    Task<SyncImportSummary> ImportAsync(
        string packagePath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SyncConflict>> ListConflictsAsync(
        CancellationToken cancellationToken = default);

    Task ResolveConflictAsync(
        Guid conflictId,
        SyncResolution resolution,
        CancellationToken cancellationToken = default);
}

public enum SyncResolution
{
    KeepLocal = 1,
    UseRemote = 2,
}

public sealed record SyncImportSummary(
    Guid SourceDeviceId,
    int ImportedItems,
    int UpdatedItems,
    int UnchangedItems,
    int Conflicts,
    int ImportedAssets);

public sealed record SyncConflict(
    Guid Id,
    Guid ItemId,
    Guid SourceDeviceId,
    string LocalTitle,
    string RemoteTitle,
    DateTimeOffset CreatedAt);
