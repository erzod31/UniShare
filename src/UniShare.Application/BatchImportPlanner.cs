namespace UniShare.Application;

public static class BatchImportPlanner
{
    public static BatchImportPlan Create(
        IEnumerable<string>? filePaths,
        string? droppedText,
        IReadOnlyCollection<string>? collections = null)
    {
        var requests = new List<CreateItemRequest>();
        var rejected = new List<string>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenUrls = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rawPath in filePaths ?? [])
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                continue;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(rawPath.Trim());
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                rejected.Add(rawPath);
                continue;
            }

            if (!seenFiles.Add(fullPath))
            {
                continue;
            }

            var title = Path.GetFileNameWithoutExtension(fullPath);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = Path.GetFileName(fullPath);
            }

            requests.Add(new CreateItemRequest(
                title,
                Url: null,
                FilePath: fullPath,
                Collections: collections));
        }

        foreach (var rawLine in SplitLines(droppedText))
        {
            var candidate = rawLine.Trim();
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https"))
            {
                rejected.Add(candidate);
                continue;
            }

            var normalized = uri.AbsoluteUri;
            if (!seenUrls.Add(normalized))
            {
                continue;
            }

            requests.Add(new CreateItemRequest(
                LinkCaptureDefaults.TitleFromUrl(uri),
                normalized,
                FilePath: null,
                Collections: collections));
        }

        return new BatchImportPlan(requests, rejected);
    }

    private static string[] SplitLines(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed record BatchImportPlan(
    IReadOnlyList<CreateItemRequest> Requests,
    IReadOnlyList<string> Rejected);
