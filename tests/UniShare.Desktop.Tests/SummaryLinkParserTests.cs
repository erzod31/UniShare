namespace UniShare.Desktop.Tests;

public sealed class SummaryLinkParserTests
{
    [Fact]
    public void ParseMakesOnlyHttpLinksClickableAndPreservesTheOriginalText()
    {
        const string text = "Documentación: https://example.com/guide?q=1. Correo ftp://example.com/file";

        var segments = SummaryLinkParser.Parse(text);

        Assert.Equal(text, string.Concat(segments.Select(segment => segment.Text)));
        var link = Assert.Single(segments, segment => segment.Uri is not null);
        Assert.Equal("https://example.com/guide?q=1", link.Text);
        Assert.Equal("https://example.com/guide?q=1", link.Uri!.AbsoluteUri);
    }

    [Fact]
    public void ParseRemovesUnbalancedClosingPunctuationButKeepsBalancedUrlParentheses()
    {
        const string text = "Mira (https://example.com/wiki/Function_(math)).";

        var link = Assert.Single(SummaryLinkParser.Parse(text), segment => segment.Uri is not null);

        Assert.Equal("https://example.com/wiki/Function_(math)", link.Text);
    }

    [Fact]
    public void ParseAcceptsMultipleLinksAcrossLines()
    {
        const string text = "Uno https://one.example/path\nDos http://two.example/path";

        var links = SummaryLinkParser.Parse(text).Where(segment => segment.Uri is not null).ToArray();

        Assert.Equal(2, links.Length);
        Assert.Equal("https://one.example/path", links[0].Uri!.AbsoluteUri);
        Assert.Equal("http://two.example/path", links[1].Uri!.AbsoluteUri);
    }
}
