namespace UniShare.Domain;

public sealed record AssetManifest
{
    public AssetManifest(
        Guid id,
        Guid itemId,
        string sha256,
        long byteLength,
        string mimeType,
        string originalName,
        string relativePath,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || itemId == Guid.Empty)
        {
            throw new ArgumentException("Los IDs del asset deben ser UUID válidos.");
        }

        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("El SHA-256 debe contener 64 caracteres hexadecimales.", nameof(sha256));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(byteLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        Id = id;
        ItemId = itemId;
        Sha256 = sha256.ToLowerInvariant();
        ByteLength = byteLength;
        MimeType = mimeType;
        OriginalName = Path.GetFileName(originalName);
        RelativePath = relativePath.Replace('\\', '/');
        CreatedAt = createdAt.ToUniversalTime();
    }

    public Guid Id { get; }

    public Guid ItemId { get; }

    public string Sha256 { get; }

    public long ByteLength { get; }

    public string MimeType { get; }

    public string OriginalName { get; }

    public string RelativePath { get; }

    public DateTimeOffset CreatedAt { get; }
}

