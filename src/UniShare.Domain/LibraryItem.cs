namespace UniShare.Domain;

public sealed record LibraryItem
{
    private const int MaximumTitleLength = 500;

    private LibraryItem(
        Guid id,
        ItemKind kind,
        string title,
        string? originalUrl,
        string? canonicalUrl,
        string? source,
        string? author,
        string? description,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? deletedAt,
        bool favorite)
    {
        Id = id;
        Kind = kind;
        Title = title;
        OriginalUrl = originalUrl;
        CanonicalUrl = canonicalUrl;
        Source = source;
        Author = author;
        Description = description;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        DeletedAt = deletedAt;
        Favorite = favorite;
    }

    public Guid Id { get; }

    public ItemKind Kind { get; }

    public string Title { get; }

    public string? OriginalUrl { get; }

    public string? CanonicalUrl { get; }

    public string? Source { get; }

    public string? Author { get; }

    public string? Description { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public DateTimeOffset? DeletedAt { get; }

    public bool Favorite { get; }

    public bool IsDeleted => DeletedAt.HasValue;

    public static LibraryItem Create(
        ItemKind kind,
        string title,
        string? originalUrl,
        DateTimeOffset now,
        Guid? id = null,
        string? source = null,
        string? author = null,
        string? description = null,
        bool favorite = false)
    {
        var normalizedTitle = NormalizeTitle(title);
        var normalizedUrl = NormalizeOptional(originalUrl);
        ValidateKind(kind, normalizedUrl);

        return new LibraryItem(
            id ?? Guid.NewGuid(),
            kind,
            normalizedTitle,
            normalizedUrl,
            normalizedUrl is null ? null : UrlCanonicalizer.Canonicalize(normalizedUrl),
            NormalizeOptional(source),
            NormalizeOptional(author),
            NormalizeOptional(description),
            now.ToUniversalTime(),
            now.ToUniversalTime(),
            null,
            favorite);
    }

    public LibraryItem UpdateMetadata(
        string title,
        string? source,
        string? author,
        string? description,
        bool favorite,
        DateTimeOffset now)
    {
        return new LibraryItem(
            Id,
            Kind,
            NormalizeTitle(title),
            OriginalUrl,
            CanonicalUrl,
            NormalizeOptional(source),
            NormalizeOptional(author),
            NormalizeOptional(description),
            CreatedAt,
            now.ToUniversalTime(),
            DeletedAt,
            favorite);
    }

    public LibraryItem Archive(DateTimeOffset now) => IsDeleted
        ? this
        : new LibraryItem(
            Id, Kind, Title, OriginalUrl, CanonicalUrl, Source, Author, Description,
            CreatedAt, now.ToUniversalTime(), now.ToUniversalTime(), Favorite);

    public LibraryItem Restore(DateTimeOffset now) => !IsDeleted
        ? this
        : new LibraryItem(
            Id, Kind, Title, OriginalUrl, CanonicalUrl, Source, Author, Description,
            CreatedAt, now.ToUniversalTime(), null, Favorite);

    public static LibraryItem Rehydrate(
        Guid id,
        ItemKind kind,
        string title,
        string? originalUrl,
        string? canonicalUrl,
        string? source,
        string? author,
        string? description,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? deletedAt,
        bool favorite)
    {
        return new LibraryItem(
            id,
            kind,
            NormalizeTitle(title),
            NormalizeOptional(originalUrl),
            NormalizeOptional(canonicalUrl),
            NormalizeOptional(source),
            NormalizeOptional(author),
            NormalizeOptional(description),
            createdAt.ToUniversalTime(),
            updatedAt.ToUniversalTime(),
            deletedAt?.ToUniversalTime(),
            favorite);
    }

    private static string NormalizeTitle(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var value = title.Trim();
        return value.Length <= MaximumTitleLength
            ? value
            : throw new ArgumentOutOfRangeException(nameof(title), $"El título no puede superar {MaximumTitleLength} caracteres.");
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidateKind(ItemKind kind, string? originalUrl)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (kind is ItemKind.Link or ItemKind.Hybrid)
        {
            _ = UrlCanonicalizer.ParseHttpUrl(originalUrl ?? throw new ArgumentException(
                "Los enlaces y elementos híbridos necesitan una URL.", nameof(originalUrl)));
        }

        if (kind == ItemKind.File && originalUrl is not null)
        {
            throw new ArgumentException("Un elemento de sólo archivo no puede incluir URL.", nameof(originalUrl));
        }
    }
}

