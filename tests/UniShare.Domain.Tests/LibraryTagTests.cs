using UniShare.Domain;

namespace UniShare.Domain.Tests;

public sealed class LibraryTagTests
{
    [Fact]
    public void NormalizeCollapsesWhitespaceAndUsesCompatibilityNormalization()
    {
        var (name, normalized) = LibraryTag.Normalize("  Ciencia   de  datos  ");

        Assert.Equal("Ciencia de datos", name);
        Assert.Equal("ciencia de datos", normalized);
    }

    [Fact]
    public void NormalizeRejectsBlankNames()
    {
        Assert.Throws<ArgumentException>(() => LibraryTag.Normalize("   "));
    }
}
