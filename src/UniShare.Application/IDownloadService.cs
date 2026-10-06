using UniShare.Domain;

namespace UniShare.Application;

public interface IDownloadService
{
    Task<WebCaptureMetadata?> FetchMetadataAsync(
        Uri sourceUri,
        CancellationToken cancellationToken = default);

    Task<DownloadJob> QueueAsync(Guid itemId, Uri sourceUri, CancellationToken cancellationToken = default);

    Task<DownloadJob> RunAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<DownloadJob> CancelAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DownloadJob>> ListAsync(CancellationToken cancellationToken = default);
}
