namespace UniShare.Domain;

public sealed record DownloadJob(
    Guid Id,
    Guid ItemId,
    Uri SourceUri,
    DownloadJobState State,
    long BytesReceived,
    long? TotalBytes,
    string? ETag,
    DateTimeOffset? LastModified,
    Guid? AssetId,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool CanRun => State is DownloadJobState.Queued or DownloadJobState.Running or
        DownloadJobState.Paused or DownloadJobState.Failed;
}
