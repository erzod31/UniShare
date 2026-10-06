namespace UniShare.Application;

public sealed record CreateItemRequest(
    string Title,
    string? Url,
    string? FilePath,
    string? Source = null,
    string? Author = null,
    string? Description = null,
    bool Favorite = false,
    IReadOnlyCollection<string>? Tags = null,
    IReadOnlyCollection<string>? Collections = null);

