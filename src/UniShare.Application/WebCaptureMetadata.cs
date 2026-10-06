namespace UniShare.Application;

public sealed record WebCaptureMetadata(
    string? Title,
    string? Source,
    string? Author,
    string? Description,
    string? PromotionalDescription = null)
{
    public string? ResolveDescription(string? currentDescription) =>
        string.IsNullOrWhiteSpace(currentDescription) ||
        ContentSummaryQuality.IsKnownNavigationJunk(currentDescription) ||
        (!string.IsNullOrWhiteSpace(PromotionalDescription) && string.Equals(
            currentDescription.Trim(), PromotionalDescription.Trim(), StringComparison.Ordinal))
            ? Description
            : currentDescription;
}
