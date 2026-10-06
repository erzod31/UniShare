using UniShare.Domain;

namespace UniShare.Application;

public sealed record ItemSearchOptions(
    string? Query = null,
    IReadOnlySet<ItemKind>? Kinds = null,
    bool IncludeDeleted = false,
    bool DeletedOnly = false,
    bool FavoriteOnly = false,
    bool? HasLocalAsset = null,
    DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null,
    string? Collection = null,
    int Limit = 200,
    int Offset = 0);

