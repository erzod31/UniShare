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
        var exportedAssetPath = Path.Combine(vault, "UniShare", "assets", $"{asset.Id:D}.pdf");
        var text = await File.ReadAllTextAsync(notePath, TestContext.Current.CancellationToken);
        text = text.Replace("nota inicial", "texto editado en Obsidian");
        await File.WriteAllTextAsync(notePath, text, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(
            exportedAssetPath,
            Enumerable.Repeat((byte)'x', checked((int)asset.ByteLength)).ToArray(),
            TestContext.Current.CancellationToken);
        var second = await vaultService.ExportAsync(vault, TestContext.Current.CancellationToken);

        Assert.Equal(1, first.NoteCount);
        Assert.Equal(1, first.AssetCount);
        Assert.Equal(1, second.PreservedNoteCount);
        Assert.Contains("texto editado en Obsidian", await File.ReadAllTextAsync(notePath, TestContext.Current.CancellationToken));
        Assert.Equal(
            await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken),
            await File.ReadAllBytesAsync(exportedAssetPath, TestContext.Current.CancellationToken));
        var index = await File.ReadAllTextAsync(Path.Combine(vault, "UniShare", "Índice.md"), TestContext.Current.CancellationToken);
        Assert.Contains("# Biblioteca UniShare", index, StringComparison.Ordinal);
        Assert.Contains($"[[{item.Id:D}|Documento]]", index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportRemovesOnlyStaleManagedNotesAndTheirReferencedAssets()
    {
        using var temporary = new TemporaryDirectory();
        var (library, repository, _, vaultService) = await CreateAsync(temporary.Path);
        var source = Path.Combine(temporary.Path, "stale.txt");
        await File.WriteAllTextAsync(source, "contenido", TestContext.Current.CancellationToken);
        var item = await library.CreateAsync(
            new CreateItemRequest("Temporal", null, source),
            TestContext.Current.CancellationToken);
        var asset = Assert.Single(await repository.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));
        var vault = Path.Combine(temporary.Path, "vault");
        Directory.CreateDirectory(vault);
        await vaultService.ExportAsync(vault, TestContext.Current.CancellationToken);

        var notesRoot = Path.Combine(vault, "UniShare");
        var assetsRoot = Path.Combine(notesRoot, "assets");
        var managedNote = Path.Combine(notesRoot, $"{item.Id:D}.md");
        var managedAsset = Path.Combine(assetsRoot, $"{asset.Id:D}.txt");
        var personalNote = Path.Combine(notesRoot, "Mis notas.md");
        var coincidentalGuidNote = Path.Combine(notesRoot, $"{Guid.NewGuid():D}.md");
        var personalAsset = Path.Combine(assetsRoot, "personal.png");
        await File.WriteAllTextAsync(personalNote, "No administrado", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(coincidentalGuidNote, "No administrado", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(personalAsset, "No administrado", TestContext.Current.CancellationToken);
        await library.ArchiveAsync(item.Id, TestContext.Current.CancellationToken);

        var result = await vaultService.ExportAsync(vault, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.NoteCount);
        Assert.False(File.Exists(managedNote));
        Assert.False(File.Exists(managedAsset));
        Assert.True(File.Exists(personalNote));
        Assert.True(File.Exists(coincidentalGuidNote));
        Assert.True(File.Exists(personalAsset));
    }

    [Fact]
    public async Task ExportPreservesStaleManagedAssetReferencedByAPersonalNote()
    {
        using var temporary = new TemporaryDirectory();
        var (library, repository, _, vaultService) = await CreateAsync(temporary.Path);
        var source = Path.Combine(temporary.Path, "referenced.txt");
        await File.WriteAllTextAsync(source, "contenido", TestContext.Current.CancellationToken);
        var item = await library.CreateAsync(
            new CreateItemRequest("Referenciado", null, source),
            TestContext.Current.CancellationToken);
        var asset = Assert.Single(await repository.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));
        var vault = Path.Combine(temporary.Path, "vault");
        Directory.CreateDirectory(vault);
        await vaultService.ExportAsync(vault, TestContext.Current.CancellationToken);

        var exportedAsset = Path.Combine(vault, "UniShare", "assets", $"{asset.Id:D}.txt");
        await File.WriteAllTextAsync(
            Path.Combine(vault, "Nota personal.md"),
            $"Referencia personal: [[UniShare/assets/{asset.Id:D}.txt]]",
            TestContext.Current.CancellationToken);
        await library.ArchiveAsync(item.Id, TestContext.Current.CancellationToken);

        await vaultService.ExportAsync(vault, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(exportedAsset));
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

    [Fact]
    public async Task ExportUpdatesOnlyTheManagedBlockAndPreservesOtherTextExactly()
    {
        using var temporary = new TemporaryDirectory();
        var (library, _, _, vaultService) = await CreateAsync(temporary.Path);
        var item = await library.CreateAsync(new CreateItemRequest(
            "Antes", "https://example.com/note", null, Description: "inicial"), TestContext.Current.CancellationToken);
        var vault = Path.Combine(temporary.Path, "vault");
        Directory.CreateDirectory(vault);
        await vaultService.ExportAsync(vault, TestContext.Current.CancellationToken);
        var path = Path.Combine(vault, "UniShare", $"{item.Id:D}.md");
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        const string notes = "  nota personal  \n\n  segunda línea  ";
        const string appendix = "\n## Apéndice personal\nTexto fuera de los marcadores.\n";
        text = text.Replace("inicial", notes, StringComparison.Ordinal)
            .Replace($"unishare_id: {item.Id:D}", $"unishare_id: {item.Id:D}\npersonal: conservar", StringComparison.Ordinal) + appendix;
        await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
        await library.UpdateAsync(item.Id, "Después", null, null, "inicial", false, TestContext.Current.CancellationToken);

        await vaultService.ExportAsync(vault, TestContext.Current.CancellationToken);

        var result = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("# Después", result, StringComparison.Ordinal);
        Assert.Contains("personal: conservar", result, StringComparison.Ordinal);
        Assert.Contains(notes, result, StringComparison.Ordinal);
        Assert.EndsWith(appendix, result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeletedItemKeepsUnimportedNotesAndAttachmentsWithoutChangingTheItem()
    {
        using var temporary = new TemporaryDirectory();
        var (library, repository, _, vaultService) = await CreateAsync(temporary.Path);
        var source = Path.Combine(temporary.Path, "personal.txt");
        await File.WriteAllTextAsync(source, "Adjunto de la nota", TestContext.Current.CancellationToken);
        var item = await library.CreateAsync(new CreateItemRequest(
            "Nota retirada", null, source, Description: "inicial"), TestContext.Current.CancellationToken);
        var asset = Assert.Single(await repository.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));
        var vault = Path.Combine(temporary.Path, "vault");
        Directory.CreateDirectory(vault);
        await vaultService.ExportAsync(vault, TestContext.Current.CancellationToken);
        var note = Path.Combine(vault, "UniShare", $"{item.Id:D}.md");
        var changed = (await File.ReadAllTextAsync(note, TestContext.Current.CancellationToken))
            .Replace("inicial", "Edición todavía no importada", StringComparison.Ordinal);
        await File.WriteAllTextAsync(note, changed, TestContext.Current.CancellationToken);
        await library.ArchiveAsync(item.Id, TestContext.Current.CancellationToken);

        var exported = await vaultService.ExportAsync(vault, TestContext.Current.CancellationToken);

        Assert.Equal(0, exported.NoteCount);
        Assert.Equal(1, exported.PreservedNoteCount);
        Assert.Equal(changed, await File.ReadAllTextAsync(note, TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(vault, "UniShare", "assets", $"{asset.Id:D}.txt")));
        var imported = await vaultService.ImportNotesAsync(vault, TestContext.Current.CancellationToken);
        Assert.Equal(0, imported.UpdatedItemCount);
        Assert.Equal(1, imported.IgnoredFileCount);
        var archived = await repository.GetAsync(item.Id, TestContext.Current.CancellationToken);
        Assert.True(archived!.IsDeleted);
        Assert.Equal("inicial", archived.Description);
    }

    [Theory]
    [InlineData("header")]
    [InlineData("heading")]
    [InlineData("appendix")]
    public async Task RetiredNotesKeepPersonalContentOutsideAnEmptyEditableBlock(string location)
    {
        using var temporary = new TemporaryDirectory();
        var (library, _, _, vaultService) = await CreateAsync(temporary.Path);
        var item = await library.CreateAsync(new CreateItemRequest(
            "Retirado", "https://example.com/retired", null), TestContext.Current.CancellationToken);
        var vault = Path.Combine(temporary.Path, "vault");
        Directory.CreateDirectory(vault);
        await vaultService.ExportAsync(vault, TestContext.Current.CancellationToken);
        var path = Path.Combine(vault, "UniShare", $"{item.Id:D}.md");
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        text = location switch
        {
            "header" => text.Replace($"unishare_id: {item.Id:D}", $"unishare_id: {item.Id:D}\npersonal: conservar", StringComparison.Ordinal),
            "heading" => text.Replace("## Notas editables", "## Mi sección personal", StringComparison.Ordinal),
            _ => text + "\nApéndice privado que no se ha importado.",
        };
        await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
        await library.ArchiveAsync(item.Id, TestContext.Current.CancellationToken);

        var result = await vaultService.ExportAsync(vault, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.NoteCount);
        Assert.Equal(1, result.PreservedNoteCount);
        Assert.Equal(text, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportRejectsUnownedDestinationsWithoutChangingTheirBytes(bool index)
    {
        using var temporary = new TemporaryDirectory();
        var (library, _, _, vaultService) = await CreateAsync(temporary.Path);
        var item = await library.CreateAsync(new CreateItemRequest(
            "Exportado", "https://example.com/personal", null), TestContext.Current.CancellationToken);
        var vault = Path.Combine(temporary.Path, "vault");
        var notes = Path.Combine(vault, "UniShare");
        Directory.CreateDirectory(notes);
        var path = Path.Combine(notes, index ? "Índice.md" : $"{item.Id:D}.md");
        const string personal = "# Documento personal\nNo pertenece a UniShare.";
        await File.WriteAllTextAsync(path, personal, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            vaultService.ExportAsync(vault, TestContext.Current.CancellationToken));

        Assert.Equal(personal, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("order")]
    [InlineData("duplicate")]
    [InlineData("duplicate-identity")]
    public async Task ImportIgnoresNotesWithoutUnambiguousOwnershipAndBoundaries(string corruption)
    {
        using var temporary = new TemporaryDirectory();
        var (library, repository, _, vaultService) = await CreateAsync(temporary.Path);
        var item = await library.CreateAsync(new CreateItemRequest(
            "Conservar", "https://example.com/markers", null, Description: "original"), TestContext.Current.CancellationToken);
        var vault = Path.Combine(temporary.Path, "vault");
        Directory.CreateDirectory(vault);
        await vaultService.ExportAsync(vault, TestContext.Current.CancellationToken);
        var path = Path.Combine(vault, "UniShare", $"{item.Id:D}.md");
        var text = (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))
            .Replace("original", "No importar", StringComparison.Ordinal);
        text = corruption switch
        {
            "identity" => text.Replace($"unishare_id: {item.Id:D}", $"unishare_id: {Guid.NewGuid():D}", StringComparison.Ordinal),
            "duplicate-identity" => text.Replace($"unishare_id: {item.Id:D}", $"unishare_id: {item.Id:D}\nunishare_id: {Guid.NewGuid():D}", StringComparison.Ordinal),
            "order" => text.Replace("<!-- unishare:managed:end -->", string.Empty, StringComparison.Ordinal) + "<!-- unishare:managed:end -->",
            _ => text + "<!-- unishare:notes:start -->",
        };
        await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);

        var result = await vaultService.ImportNotesAsync(vault, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.UpdatedItemCount);
        Assert.Equal(1, result.IgnoredFileCount);
        Assert.Equal("original", (await repository.GetAsync(item.Id, TestContext.Current.CancellationToken))!.Description);
        await Assert.ThrowsAsync<InvalidDataException>(() => vaultService.ExportAsync(vault, TestContext.Current.CancellationToken));
        Assert.Equal(text, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
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
