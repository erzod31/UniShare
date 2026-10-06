namespace UniShare.Application;

public interface IBackupService
{
    Task<BackupSummary> ExportAsync(string destinationPath, CancellationToken cancellationToken = default);

    Task<BackupSummary> InspectAsync(string backupPath, CancellationToken cancellationToken = default);

    Task<BackupSummary> RestoreToNewProfileAsync(
        string backupPath,
        string destinationProfile,
        CancellationToken cancellationToken = default);
}

public interface ISelectiveBackupService
{
    Task<BackupSummary> ExportAsync(
        string destinationPath,
        IReadOnlySet<string> includedBlobHashes,
        IReadOnlySet<string> availableBlobHashes,
        CancellationToken cancellationToken = default);
}

public interface IDeltaBackupService
{
    Task<BackupSummary> ExportDeltaAsync(
        string destinationPath,
        IReadOnlySet<string> includedItemIds,
        IReadOnlySet<string> includedBlobHashes,
        IReadOnlySet<string> availableBlobHashes,
        CancellationToken cancellationToken = default);
}

public sealed record BackupSummary(
    int FormatVersion,
    DateTimeOffset CreatedAtUtc,
    int ItemCount,
    int AssetCount,
    int TagCount,
    int CollectionCount,
    int BlobCount,
    long TotalBlobBytes);

