using System.Text;
using UniShare.Application;
using UniShare.Infrastructure;

namespace UniShare.Infrastructure.Tests;

public sealed class LibraryServiceIntegrationTests
{
    [Fact]
    public async Task CreateFileAndHybridPersistSeparateAssetsButSharePhysicalBlob()
    {
        using var temporary = new TemporaryDirectory();
        var sourcePath = System.IO.Path.Combine(temporary.Path, "paper.pdf");
        await File.WriteAllBytesAsync(sourcePath, Encoding.ASCII.GetBytes("%PDF-1.7 test fixture"), TestContext.Current.CancellationToken);
        var profilePath = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profilePath, "library.db"));
        var blobStore = new FileBlobStore(profilePath);
        var service = new LibraryService(repository, blobStore, new FixedTimeProvider());
        await service.InitializeAsync(TestContext.Current.CancellationToken);

        var file = await service.CreateAsync(new CreateItemRequest("PDF local", null, sourcePath), TestContext.Current.CancellationToken);
        var hybrid = await service.CreateAsync(new CreateItemRequest(
            "PDF con origen",
            "https://example.com/paper",
            sourcePath,
            Tags: ["Investigación"],
            Collections: ["Universidad"]), TestContext.Current.CancellationToken);

        var items = await service.SearchAsync(null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, items.Count);
        var fileAsset = Assert.Single(await service.GetAssetsAsync(file.Id, TestContext.Current.CancellationToken));
        var hybridAsset = Assert.Single(await service.GetAssetsAsync(hybrid.Id, TestContext.Current.CancellationToken));
        Assert.NotEqual(fileAsset.Id, hybridAsset.Id);
        Assert.Equal(fileAsset.Sha256, hybridAsset.Sha256);
        Assert.True(await blobStore.VerifyAsync(fileAsset.Sha256, TestContext.Current.CancellationToken));
        var organization = await service.GetOrganizationAsync(hybrid.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Investigación", Assert.Single(organization.Tags).Name);
        Assert.Equal("Universidad", Assert.Single(organization.Collections).Name);
        Assert.Single(Directory.EnumerateFiles(
            System.IO.Path.Combine(profilePath, "blobs"), "*", SearchOption.AllDirectories));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    }
}
