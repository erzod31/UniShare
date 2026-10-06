using UniShare.Domain;

namespace UniShare.Application;

public interface IDownloadJobRepository
{
    Task<DownloadJob> QueueDownloadAsync(
        Guid itemId,
        Uri sourceUri,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<DownloadJob?> GetDownloadAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DownloadJob>> ListDownloadsAsync(CancellationToken cancellationToken = default);

    Task UpdateDownloadAsync(
        Guid jobId,
        DownloadJobState state,
        long bytesReceived,
        long? totalBytes,
        string? etag,
        DateTimeOffset? lastModified,
        string? errorCode,
        string? errorMessage,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task CompleteDownloadAsync(
        Guid jobId,
        AssetManifest asset,
        WebCaptureMetadata? metadata,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
