using System.Text;
using UniShare.Application;

namespace UniShare.Infrastructure.Tests;

public sealed class PortableSyncServiceTests
{
    [Fact]
    public async Task ImportIsBidirectionalIdempotentAndTransfersBlobs()
    {
        using var temporary = new TemporaryDirectory();
        var left = await CreateProfileAsync(temporary.Path, "left");
        var right = await CreateProfileAsync(temporary.Path, "right");
        var sourceFile = Path.Combine(temporary.Path, "evidence.txt");
        await File.WriteAllTextAsync(sourceFile, "contenido sincronizado", Encoding.UTF8, TestContext.Current.CancellationToken);
        var created = await left.Library.CreateAsync(new CreateItemRequest(
            "Documento compartido", "https://example.com/shared", sourceFile,
            Tags: ["Investigación"], Collections: ["Trabajo / UniShare"]), TestContext.Current.CancellationToken);
        var package = Path.Combine(temporary.Path, "left-1.zip");
        await left.Backup.ExportAsync(package, TestContext.Current.CancellationToken);

        var first = await right.Sync.ImportAsync(package, TestContext.Current.CancellationToken);
        var second = await right.Sync.ImportAsync(package, TestContext.Current.CancellationToken);

        Assert.Equal(1, first.ImportedItems);
        Assert.Equal(1, first.ImportedAssets);
        Assert.Equal(1, second.UnchangedItems);
        Assert.Equal(0, second.Conflicts);
        var imported = Assert.Single(await right.Library.SearchAsync(null, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(created, imported);
        var asset = Assert.Single(await right.Library.GetAssetsAsync(created.Id, TestContext.Current.CancellationToken));
        Assert.True(await new FileBlobStore(right.Root).VerifyAsync(asset.Sha256, TestContext.Current.CancellationToken));
        var organization = await right.Library.GetOrganizationAsync(created.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Investigación", Assert.Single(organization.Tags).Name);
        Assert.Equal("Trabajo / UniShare", Assert.Single(organization.Collections).Name);

        var returnPackage = Path.Combine(temporary.Path, "right-1.zip");
        await right.Backup.ExportAsync(returnPackage, TestContext.Current.CancellationToken);
        var returned = await left.Sync.ImportAsync(returnPackage, TestContext.Current.CancellationToken);
        Assert.Equal(1, returned.UnchangedItems);
        Assert.Empty(await left.Sync.ListConflictsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnilateralChangeIsAppliedButConcurrentChangesCreateResolvableConflict()
    {
        using var temporary = new TemporaryDirectory();
        var left = await CreateProfileAsync(temporary.Path, "left");
        var right = await CreateProfileAsync(temporary.Path, "right");
        var item = await left.Library.CreateAsync(new CreateItemRequest(
            "Versión inicial", "https://example.com/versioned", null), TestContext.Current.CancellationToken);

        var baseline = Path.Combine(temporary.Path, "baseline.zip");
        await left.Backup.ExportAsync(baseline, TestContext.Current.CancellationToken);
        await right.Sync.ImportAsync(baseline, TestContext.Current.CancellationToken);

        await left.Library.UpdateAsync(item.Id, "Cambio unilateral", null, null, "desde Windows", false, TestContext.Current.CancellationToken);
        var unilateral = Path.Combine(temporary.Path, "unilateral.zip");
        await left.Backup.ExportAsync(unilateral, TestContext.Current.CancellationToken);
        var updated = await right.Sync.ImportAsync(unilateral, TestContext.Current.CancellationToken);
        Assert.Equal(1, updated.UpdatedItems);
        Assert.Equal("Cambio unilateral", Assert.Single(await right.Library.SearchAsync(null, cancellationToken: TestContext.Current.CancellationToken)).Title);

        await left.Library.UpdateAsync(item.Id, "Cambio izquierdo", null, null, "izquierda", false, TestContext.Current.CancellationToken);
        await right.Library.UpdateAsync(item.Id, "Cambio derecho", null, null, "derecha", true, TestContext.Current.CancellationToken);
        var concurrent = Path.Combine(temporary.Path, "concurrent.zip");
        await left.Backup.ExportAsync(concurrent, TestContext.Current.CancellationToken);
        var conflicted = await right.Sync.ImportAsync(concurrent, TestContext.Current.CancellationToken);

        Assert.Equal(1, conflicted.Conflicts);
        Assert.Equal("Cambio derecho", Assert.Single(await right.Library.SearchAsync(null, cancellationToken: TestContext.Current.CancellationToken)).Title);
        var conflict = Assert.Single(await right.Sync.ListConflictsAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Cambio derecho", conflict.LocalTitle);
        Assert.Equal("Cambio izquierdo", conflict.RemoteTitle);

        await right.Sync.ResolveConflictAsync(conflict.Id, SyncResolution.UseRemote, TestContext.Current.CancellationToken);
        Assert.Empty(await right.Sync.ListConflictsAsync(TestContext.Current.CancellationToken));
        var resolved = Assert.Single(await right.Library.SearchAsync(null, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("Cambio izquierdo", resolved.Title);
        Assert.Equal("izquierda", resolved.Description);
        Assert.False(resolved.Favorite);
    }

    [Fact]
    public async Task FirstDivergentEncounterNeverOverwritesEitherVersionSilently()
    {
        using var temporary = new TemporaryDirectory();
        var left = await CreateProfileAsync(temporary.Path, "left");
        var right = await CreateProfileAsync(temporary.Path, "right");
        var item = await left.Library.CreateAsync(new CreateItemRequest(
            "Original", "https://example.com/conflict", null), TestContext.Current.CancellationToken);
        var initial = Path.Combine(temporary.Path, "initial.zip");
        await left.Backup.ExportAsync(initial, TestContext.Current.CancellationToken);
        await right.Sync.ImportAsync(initial, TestContext.Current.CancellationToken);

        await right.Library.UpdateAsync(item.Id, "Local conservado", null, null, null, false, TestContext.Current.CancellationToken);
        var freshPeer = await CreateProfileAsync(temporary.Path, "fresh-peer");
        await freshPeer.Sync.ImportAsync(initial, TestContext.Current.CancellationToken);
        await freshPeer.Library.UpdateAsync(item.Id, "Remoto conservado", null, null, null, false, TestContext.Current.CancellationToken);
        var divergent = Path.Combine(temporary.Path, "divergent.zip");
        await freshPeer.Backup.ExportAsync(divergent, TestContext.Current.CancellationToken);

        var result = await right.Sync.ImportAsync(divergent, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Conflicts);
        Assert.Equal("Local conservado", Assert.Single(await right.Library.SearchAsync(null, cancellationToken: TestContext.Current.CancellationToken)).Title);
        var conflict = Assert.Single(await right.Sync.ListConflictsAsync(TestContext.Current.CancellationToken));
        await right.Sync.ResolveConflictAsync(conflict.Id, SyncResolution.KeepLocal, TestContext.Current.CancellationToken);
        Assert.Equal("Local conservado", Assert.Single(await right.Library.SearchAsync(null, cancellationToken: TestContext.Current.CancellationToken)).Title);
    }

    [Fact]
    public async Task ObservedMembershipRemovalConvergesWithoutRemovingConcurrentReAdd()
    {
        using var temporary = new TemporaryDirectory();
        var left = await CreateProfileAsync(temporary.Path, "left");
        var right = await CreateProfileAsync(temporary.Path, "right");
        var item = await left.Library.CreateAsync(new CreateItemRequest(
            "Organización causal", "https://example.com/orset", null,
            Tags: ["Pendiente"], Collections: ["Lecturas"]), TestContext.Current.CancellationToken);

        var baseline = Path.Combine(temporary.Path, "membership-baseline.zip");
        await left.Backup.ExportAsync(baseline, TestContext.Current.CancellationToken);
        await right.Sync.ImportAsync(baseline, TestContext.Current.CancellationToken);

        await left.Library.ReplaceOrganizationAsync(item.Id, [], [], TestContext.Current.CancellationToken);
        var removed = Path.Combine(temporary.Path, "membership-removed.zip");
        await left.Backup.ExportAsync(removed, TestContext.Current.CancellationToken);
        await right.Sync.ImportAsync(removed, TestContext.Current.CancellationToken);
        var converged = await right.Library.GetOrganizationAsync(item.Id, TestContext.Current.CancellationToken);
        Assert.Empty(converged.Tags);
        Assert.Empty(converged.Collections);

        await right.Library.ReplaceOrganizationAsync(item.Id, ["Pendiente"], ["Lecturas"], TestContext.Current.CancellationToken);
        await right.Sync.ImportAsync(removed, TestContext.Current.CancellationToken);
        var reAdded = await right.Library.GetOrganizationAsync(item.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Pendiente", Assert.Single(reAdded.Tags).Name);
        Assert.Equal("Lecturas", Assert.Single(reAdded.Collections).Name);
    }

    private static async Task<Profile> CreateProfileAsync(string root, string name)
    {
        var profileRoot = Path.Combine(root, name);
        var repository = new SqliteLibraryRepository(Path.Combine(profileRoot, "library.db"));
        var blobStore = new FileBlobStore(profileRoot);
        var library = new LibraryService(repository, blobStore, TimeProvider.System);
        await library.InitializeAsync();
        var backup = new PortableBackupService(profileRoot);
        return new Profile(
            profileRoot,
            library,
            backup,
            new PortableSyncService(profileRoot, backup, blobStore));
    }

    private sealed record Profile(
        string Root,
        LibraryService Library,
        PortableBackupService Backup,
        PortableSyncService Sync);
}
