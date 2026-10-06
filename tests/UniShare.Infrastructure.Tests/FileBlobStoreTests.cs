using System.Text;
using UniShare.Infrastructure;

namespace UniShare.Infrastructure.Tests;

public sealed class FileBlobStoreTests
{
    [Fact]
    public async Task ImportUsesContentSignatureBeforeMisleadingExtension()
    {
        using var temporary = new TemporaryDirectory();
        var source = System.IO.Path.Combine(temporary.Path, "misleading.png");
        await File.WriteAllBytesAsync(source, "%PDF-1.7\ncontent"u8.ToArray(), TestContext.Current.CancellationToken);
        var store = new FileBlobStore(System.IO.Path.Combine(temporary.Path, "profile"));

        var result = await store.ImportFileAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal("application/pdf", result.MimeType);
    }

    [Fact]
    public async Task ImportFileStreamsHashesAndDeduplicatesPhysicalBlob()
    {
        using var temporary = new TemporaryDirectory();
        var firstSource = System.IO.Path.Combine(temporary.Path, "first.txt");
        var secondSource = System.IO.Path.Combine(temporary.Path, "second.txt");
        var bytes = Encoding.UTF8.GetBytes("contenido durable");
        await File.WriteAllBytesAsync(firstSource, bytes, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(secondSource, bytes, TestContext.Current.CancellationToken);
        var store = new FileBlobStore(System.IO.Path.Combine(temporary.Path, "profile"));

        var first = await store.ImportFileAsync(firstSource, TestContext.Current.CancellationToken);
        var second = await store.ImportFileAsync(secondSource, TestContext.Current.CancellationToken);

        Assert.False(first.AlreadyExisted);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(bytes.Length, first.ByteLength);
        Assert.True(await store.VerifyAsync(first.Sha256, TestContext.Current.CancellationToken));
        Assert.Single(Directory.EnumerateFiles(
            System.IO.Path.Combine(temporary.Path, "profile", "blobs"), "*", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(
            System.IO.Path.Combine(temporary.Path, "profile", "staging"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void GetAbsolutePathRejectsTraversal()
    {
        using var temporary = new TemporaryDirectory();
        var store = new FileBlobStore(System.IO.Path.Combine(temporary.Path, "profile"));

        Assert.Throws<UnauthorizedAccessException>(() => store.GetAbsolutePath("../outside.txt"));
    }
}
