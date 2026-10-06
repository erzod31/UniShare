using UniShare.Domain;

namespace UniShare.Domain.Tests;

public sealed class AssetManifestTests
{
    [Fact]
    public void ConstructorNormalizesHashNameAndRelativeSeparators()
    {
        var hash = new string('A', 64);
        var asset = new AssetManifest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            hash,
            42,
            "application/pdf",
            "../documento.pdf",
            "blobs\\aa\\bb\\hash",
            DateTimeOffset.UtcNow);

        Assert.Equal(hash.ToLowerInvariant(), asset.Sha256);
        Assert.Equal("documento.pdf", asset.OriginalName);
        Assert.Equal("blobs/aa/bb/hash", asset.RelativePath);
    }

    [Fact]
    public void ConstructorRejectsInvalidHash()
    {
        Assert.Throws<ArgumentException>(() => new AssetManifest(
            Guid.NewGuid(), Guid.NewGuid(), "invalid", 0, "text/plain", "a.txt", "blobs/a", DateTimeOffset.UtcNow));
    }
}
