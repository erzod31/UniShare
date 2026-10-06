using System.Text;
using UniShare.Application;
using UniShare.Infrastructure;

namespace UniShare.Infrastructure.Tests;

public sealed class ObsidianVaultServiceTests
{
    [Fact]
    public async Task ExportPreservesEditableNotesAndCopiesAssetsByStableId()
    {
        using var temporary = new TemporaryDirectory();
        var (library, repository, blobStore, vaultService) = await CreateAsync(temporary.Path);
        var source = Path.Combine(temporary.Path, "document.pdf");
        await File.WriteAllBytesAsync(source, Encoding.ASCII.GetBytes("%PDF-1.7 fixture"), TestContext.Current.CancellationToken);
        var item = await library.CreateAsync(new CreateItemRequest(
            "Documento",
            "https://example.com/document",
            source,
            Description: "nota inicial",
            Tags: ["lectura"],
            Collections: ["proyecto"]), TestContext.Current.CancellationToken);
        var asset = Assert.Single(await repository.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));
        var vault = Path.Combine(temporary.Path, "vault");
        Directory.CreateDirectory(vault);

        var first = await vaultService.ExportAsync(vault, TestContext.Current.CancellationToken);
        var notePath = Path.Combine(vault, "UniShare", $"{item.Id:D}.md");
        var text = await File.ReadAllTextAsync(notePath, TestContext.Current.CancellationToken);
        text = text.Replace("nota inicial", "texto editado en Obsidian");
        await File.WriteAllTextAsync(notePath, text, TestContext.Current.CancellationToken);
        var second = await vaultService.ExportAsync(vault, TestContext.Current.CancellationToken);

        Assert.Equal(1, first.NoteCount);
        Assert.Equal(1, first.AssetCount);
        Assert.Equal(1, second.PreservedNoteCount);
        Assert.Contains("texto editado en Obsidian", await File.ReadAllTextAsync(notePath, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(vault, "UniShare", "assets", $"{asset.Id:D}.pdf")));
        var index = await File.ReadAllTextAsync(Path.Combine(vault, "UniShare", "Índice.md"), TestContext.Current.CancellationToken);
        Assert.Contains("# Biblioteca UniShare", index, StringComparison.Ordinal);
        Assert.Contains($"[[{item.Id:D}|Documento]]", index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportNotesUpdatesOnlyDescriptionAndThenBecomesNoOp()
    {
        using var temporary = new TemporaryDirectory();
        var (library, repository, _, vaultService) = await CreateAsync(temporary.Path);
        var item = await library.CreateAsync(new CreateItemRequest(
            "Título conservado",
            "https://example.com",
            null,
            Author: "Autora",
            Description: "original",
            Favorite: true), TestContext.Current.CancellationToken);
        var vault = Path.Combine(temporary.Path, "vault");
        Directory.CreateDirectory(vault);
        await vaultService.ExportAsync(vault, TestContext.Current.CancellationToken);
        var notePath = Path.Combine(vault, "UniShare", $"{item.Id:D}.md");
        var text = (await File.ReadAllTextAsync(notePath, TestContext.Current.CancellationToken)).Replace("original", "cambio autorizado");
        await File.WriteAllTextAsync(notePath, text, TestContext.Current.CancellationToken);

        var first = await vaultService.ImportNotesAsync(vault, TestContext.Current.CancellationToken);
        var second = await vaultService.ImportNotesAsync(vault, TestContext.Current.CancellationToken);
        var updated = await repository.GetAsync(item.Id, TestContext.Current.CancellationToken);

        Assert.Equal(1, first.UpdatedItemCount);
        Assert.Equal(1, second.UnchangedItemCount);
        Assert.Equal("cambio autorizado", updated!.Description);
        Assert.Equal("Título conservado", updated.Title);
        Assert.Equal("Autora", updated.Author);
        Assert.True(updated.Favorite);
        Assert.Equal(0, first.IgnoredFileCount);
    }

    private static async Task<(LibraryService Library, SqliteLibraryRepository Repository,
        FileBlobStore BlobStore, ObsidianVaultService Vault)> CreateAsync(string root)
    {
        var profile = Path.Combine(root, "profile");
        var repository = new SqliteLibraryRepository(Path.Combine(profile, "library.db"));
        var blobStore = new FileBlobStore(profile);
        var time = new FixedTimeProvider();
        var library = new LibraryService(repository, blobStore, time);
        await library.InitializeAsync();
        return (library, repository, blobStore, new ObsidianVaultService(repository, blobStore, time));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    }
}
