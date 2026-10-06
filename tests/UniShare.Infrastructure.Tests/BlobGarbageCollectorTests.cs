using UniShare.Application;
using UniShare.Infrastructure;

namespace UniShare.Infrastructure.Tests;

public sealed class BlobGarbageCollectorTests
{
    [Fact]
    public async Task DeletesOnlyOldCanonicalUnreferencedBlobs()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        var store = new FileBlobStore(profile);
        var library = new LibraryService(repository, store, TimeProvider.System);
        await library.InitializeAsync(TestContext.Current.CancellationToken);

        var referencedSource = System.IO.Path.Combine(temporary.Path, "referenced.txt");
        await File.WriteAllTextAsync(referencedSource, "referenced", TestContext.Current.CancellationToken);
        var item = await library.CreateAsync(new CreateItemRequest(
            "Referenced", Url: null, FilePath: referencedSource), TestContext.Current.CancellationToken);
        var referenced = Assert.Single(await library.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));
        File.SetLastWriteTimeUtc(store.GetAbsolutePath(referenced.RelativePath), DateTime.UtcNow.AddDays(-30));

        var oldHash = new string('a', 64);
        var oldPath = store.GetAbsolutePath($"blobs/aa/aa/{oldHash}");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(oldPath)!);
        await File.WriteAllTextAsync(oldPath, "orphan", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddDays(-30));

        var youngHash = new string('b', 64);
        var youngPath = store.GetAbsolutePath($"blobs/bb/bb/{youngHash}");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(youngPath)!);
        await File.WriteAllTextAsync(youngPath, "young", TestContext.Current.CancellationToken);

        var unknownPath = System.IO.Path.Combine(profile, "blobs", "manual-note.txt");
        await File.WriteAllTextAsync(unknownPath, "keep unknown files", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(unknownPath, DateTime.UtcNow.AddDays(-30));

        var result = await new BlobGarbageCollector(profile, TimeProvider.System)
            .CollectAsync(TimeSpan.FromDays(7), TestContext.Current.CancellationToken);

        Assert.Equal(1, result.DeletedBlobs);
        Assert.Equal(6, result.ReclaimedBytes);
        Assert.False(File.Exists(oldPath));
        Assert.True(File.Exists(youngPath));
        Assert.True(File.Exists(unknownPath));
        Assert.True(await store.VerifyAsync(referenced.Sha256, TestContext.Current.CancellationToken));
    }
}
