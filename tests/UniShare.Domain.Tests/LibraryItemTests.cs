using UniShare.Domain;

namespace UniShare.Domain.Tests;

public sealed class LibraryItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ItemKind.Link, "https://example.com/article", true)]
    [InlineData(ItemKind.File, null, true)]
    [InlineData(ItemKind.Hybrid, "https://example.com/article", true)]
    [InlineData(ItemKind.Link, null, false)]
    [InlineData(ItemKind.File, "https://example.com/file", false)]
    [InlineData(ItemKind.Hybrid, null, false)]
    public void CreateEnforcesKindAndUrlInvariant(ItemKind kind, string? url, bool valid)
    {
        if (valid)
        {
            var item = LibraryItem.Create(kind, "Título", url, Now);
            Assert.Equal(kind, item.Kind);
            Assert.Equal(url, item.OriginalUrl);
        }
        else
        {
            Assert.ThrowsAny<ArgumentException>(() => LibraryItem.Create(kind, "Título", url, Now));
        }
    }

    [Fact]
    public void CreatePreservesOriginalUrlAndRemovesKnownTrackingFromCanonicalUrl()
    {
        const string original = "HTTPS://Example.COM:443/path?keep=1&utm_source=test&other=2#fragment";

        var item = LibraryItem.Create(ItemKind.Link, "Ejemplo", original, Now);

        Assert.Equal(original, item.OriginalUrl);
        Assert.Equal("https://example.com/path?keep=1&other=2", item.CanonicalUrl);
    }

    [Fact]
    public void ArchiveAndRestorePreserveIdentityAndExposeDeletionState()
    {
        var item = LibraryItem.Create(ItemKind.Link, "Ejemplo", "https://example.com", Now);

        var archived = item.Archive(Now.AddMinutes(1));
        var restored = archived.Restore(Now.AddMinutes(2));

        Assert.Equal(item.Id, archived.Id);
        Assert.True(archived.IsDeleted);
        Assert.Equal(item.Id, restored.Id);
        Assert.False(restored.IsDeleted);
        Assert.Equal(Now.AddMinutes(2), restored.UpdatedAt);
    }

    [Fact]
    public void CreateRejectsNonHttpSchemes()
    {
        Assert.Throws<ArgumentException>(() =>
            LibraryItem.Create(ItemKind.Link, "Local", "file:///c:/private.txt", Now));
    }
}
