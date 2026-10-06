namespace UniShare.Domain;

public enum DownloadJobState
{
    Queued = 1,
    Running = 2,
    Paused = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,
}
