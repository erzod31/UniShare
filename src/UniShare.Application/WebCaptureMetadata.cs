namespace UniShare.Application;

public sealed record WebCaptureMetadata(
    string? Title,
    string? Source,
    string? Author,
    string? Description,
    string? PromotionalDescription = null,
    bool OriginDescriptionFound = false,
    string? PageTitle = null)
{
    public string ResolveTitle(string currentTitle, Uri sourceUri)
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            return currentTitle;
        }
        if (currentTitle == LinkCaptureDefaults.TitleFromUrl(sourceUri))
        {
            return Title;
        }
        var host = sourceUri.IdnHost.TrimEnd('.');
        var tikTok = host.Equals("tiktok.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".tiktok.com", StringComparison.OrdinalIgnoreCase);
        // Earlier clients used TikTok's generic tab label as the item title.
        var genericTikTokTitle = currentTitle.Equals("TikTok", StringComparison.OrdinalIgnoreCase) ||
            currentTitle.Equals("TikTok - Make Your Day", StringComparison.OrdinalIgnoreCase) ||
            currentTitle.StartsWith("TikTok · ", StringComparison.OrdinalIgnoreCase) ||
            currentTitle.StartsWith("TikTok | ", StringComparison.OrdinalIgnoreCase);
        var previousCaptionTitle = OriginDescriptionFound &&
            currentTitle == LinkCaptureDefaults.LegacyTitleFromCaption(Description);
        return tikTok && (genericTikTokTitle && currentTitle == PageTitle || previousCaptionTitle) ? Title : currentTitle;
    }

    public string? ResolveDescription(string? currentDescription) =>
        string.IsNullOrWhiteSpace(currentDescription) ||
        ContentSummaryQuality.IsKnownNavigationJunk(currentDescription) ||
        (!string.IsNullOrWhiteSpace(PromotionalDescription) && string.Equals(
            currentDescription.Trim(), PromotionalDescription.Trim(), StringComparison.Ordinal))
            ? Description
            : currentDescription;
}
