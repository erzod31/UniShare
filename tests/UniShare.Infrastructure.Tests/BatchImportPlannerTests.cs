using UniShare.Application;

namespace UniShare.Infrastructure.Tests;

public sealed class BatchImportPlannerTests
{
    [Fact]
    public void CreateKeepsValidFilesAndLinksAndReportsInvalidText()
    {
        var plan = BatchImportPlanner.Create(
            [@"C:\entrada\informe.pdf", @"C:\entrada\foto.png"],
            "https://example.com/a\nesto no es una URL\nhttps://example.com/b",
            ["Proyecto A"]);

        Assert.Equal(4, plan.Requests.Count);
        Assert.Equal("informe", plan.Requests[0].Title);
        Assert.Equal(@"C:\entrada\informe.pdf", plan.Requests[0].FilePath);
        Assert.Equal("example.com", plan.Requests[2].Title);
        Assert.Equal("https://example.com/a", plan.Requests[2].Url);
        Assert.All(plan.Requests, request => Assert.Equal(["Proyecto A"], request.Collections));
        Assert.Equal(["esto no es una URL"], plan.Rejected);
    }

    [Fact]
    public void CreateDeduplicatesRepeatedInputsWithoutDiscardingDistinctUrls()
    {
        var plan = BatchImportPlanner.Create(
            [@"C:\entrada\informe.pdf", @"c:\ENTRADA\informe.pdf"],
            "https://example.com/a\nhttps://example.com/a\nhttps://example.com/A");

        Assert.Equal(3, plan.Requests.Count);
        Assert.Empty(plan.Rejected);
    }

    [Fact]
    public void CreateRejectsNonHttpSchemes()
    {
        var plan = BatchImportPlanner.Create(null, "file:///tmp/private.txt\njavascript:alert(1)");

        Assert.Empty(plan.Requests);
        Assert.Equal(2, plan.Rejected.Count);
    }
}
