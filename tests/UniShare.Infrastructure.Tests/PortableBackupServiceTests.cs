using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using UniShare.Application;
using UniShare.Infrastructure;

namespace UniShare.Infrastructure.Tests;

public sealed class PortableBackupServiceTests
{
    [Fact]
    public async Task ExportInspectAndRestoreRoundTripPreservesDataButRegeneratesDeviceIdentity()
    {
        using var temporary = new TemporaryDirectory();
        var sourceProfile = System.IO.Path.Combine(temporary.Path, "source-profile");
        var sourceFile = System.IO.Path.Combine(temporary.Path, "paper.pdf");
        await File.WriteAllBytesAsync(sourceFile, Encoding.ASCII.GetBytes("%PDF-1.7 portable backup"), TestContext.Current.CancellationToken);
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(sourceProfile, "library.db"));
        var blobStore = new FileBlobStore(sourceProfile);
        var library = new LibraryService(repository, blobStore, TimeProvider.System);
        await library.InitializeAsync(TestContext.Current.CancellationToken);
        var item = await library.CreateAsync(new CreateItemRequest(
            "Documento portable",
            "https://example.com/document",
            sourceFile,
            Source: "Revista local",
            Author: "Ada Lovelace",
            Description: "Descripción con acentos",
            Favorite: true,
            Tags: ["Investigación"],
            Collections: ["Universidad"]), TestContext.Current.CancellationToken);
        var archived = await library.CreateAsync(new CreateItemRequest(
            "Enlace archivado", "https://example.com/archive", null), TestContext.Current.CancellationToken);
        await library.ArchiveAsync(archived.Id, TestContext.Current.CancellationToken);
        var sourceDeviceId = await ReadDeviceIdAsync(System.IO.Path.Combine(sourceProfile, "library.db"));
        var backupPath = System.IO.Path.Combine(temporary.Path, "library.unishare.zip");
        var backup = new PortableBackupService(sourceProfile);

        var exported = await backup.ExportAsync(backupPath, TestContext.Current.CancellationToken);
        using (var archive = ZipFile.OpenRead(backupPath))
        await using (var manifestStream = archive.GetEntry("manifest.json")!.Open())
        {
            using var manifest = await JsonDocument.ParseAsync(manifestStream, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(
                sourceDeviceId,
                manifest.RootElement.GetProperty("source_device_id").GetGuid());
        }
        var inspected = await backup.InspectAsync(backupPath, TestContext.Current.CancellationToken);
        var restoredProfile = System.IO.Path.Combine(temporary.Path, "restored-profile");
        var restored = await backup.RestoreToNewProfileAsync(backupPath, restoredProfile, TestContext.Current.CancellationToken);

        Assert.Equal(2, exported.ItemCount);
        Assert.Equal(exported, inspected);
        Assert.Equal(exported.ItemCount, restored.ItemCount);
        var restoredRepository = new SqliteLibraryRepository(System.IO.Path.Combine(restoredProfile, "library.db"));
        await restoredRepository.InitializeAsync(TestContext.Current.CancellationToken);
        var restoredItems = await restoredRepository.SearchAsync(null, includeDeleted: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, restoredItems.Count);
        Assert.Equal(item, Assert.Single(restoredItems, value => value.Id == item.Id));
        Assert.True(Assert.Single(restoredItems, value => value.Id == archived.Id).IsDeleted);
        var restoredAsset = Assert.Single(await restoredRepository.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));
        Assert.True(await new FileBlobStore(restoredProfile).VerifyAsync(restoredAsset.Sha256, TestContext.Current.CancellationToken));
        var organization = await restoredRepository.GetOrganizationAsync(item.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Investigación", Assert.Single(organization.Tags).Name);
        Assert.Equal("Universidad", Assert.Single(organization.Collections).Name);
        Assert.Equal(item.Id, Assert.Single(await restoredRepository.SearchAsync("portable", cancellationToken: TestContext.Current.CancellationToken)).Id);
        var restoredDeviceId = await ReadDeviceIdAsync(System.IO.Path.Combine(restoredProfile, "library.db"));
        Assert.NotEqual(sourceDeviceId, restoredDeviceId);
    }

    [Fact]
    public async Task InspectRejectsModifiedEntryAndRestoreRefusesExistingDestination()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        await repository.CreateAsync(UniShare.Domain.LibraryItem.Create(
                UniShare.Domain.ItemKind.Link,
                "Original",
                "https://example.com",
                DateTimeOffset.UtcNow), null, TestContext.Current.CancellationToken);
        var service = new PortableBackupService(profile);
        var backupPath = System.IO.Path.Combine(temporary.Path, "backup.zip");
        await service.ExportAsync(backupPath, TestContext.Current.CancellationToken);
        var existingDestination = System.IO.Path.Combine(temporary.Path, "existing");
        Directory.CreateDirectory(existingDestination);

        await Assert.ThrowsAsync<IOException>(() =>
            service.RestoreToNewProfileAsync(backupPath, existingDestination, TestContext.Current.CancellationToken));

        await using (var stream = new FileStream(backupPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
        {
            archive.GetEntry("metadata/items.ndjson")!.Delete();
            var entry = archive.CreateEntry("metadata/items.ndjson");
            await using var output = entry.Open();
            await output.WriteAsync("{\"tampered\":true}\n"u8.ToArray(), TestContext.Current.CancellationToken);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => service.InspectAsync(backupPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SelectiveExportKeepsMetadataAndInventoryWhileSendingOnlyRequestedBlobs()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "selective-profile");
        var source = System.IO.Path.Combine(temporary.Path, "large.bin");
        await File.WriteAllBytesAsync(source, Encoding.UTF8.GetBytes("blob compartido"), TestContext.Current.CancellationToken);
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        var library = new LibraryService(repository, new FileBlobStore(profile), TimeProvider.System);
        await library.InitializeAsync(TestContext.Current.CancellationToken);
        var item = await library.CreateAsync(new CreateItemRequest("Blob", null, source), TestContext.Current.CancellationToken);
        var hash = Assert.Single(await library.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken)).Sha256;
        var service = new PortableBackupService(profile);
        var metadataOnly = System.IO.Path.Combine(temporary.Path, "metadata-only.zip");

        var summary = await service.ExportAsync(metadataOnly, new HashSet<string>(), new HashSet<string> { hash }, TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.AssetCount);
        Assert.Equal(0, summary.BlobCount);
        Assert.Equal(summary, await service.InspectAsync(metadataOnly, TestContext.Current.CancellationToken));
        using var archive = ZipFile.OpenRead(metadataOnly);
        Assert.NotNull(archive.GetEntry("sync/available_blobs.ndjson"));
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.StartsWith("blobs/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeltaExportContainsOnlySelectedItemAggregate()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "delta-profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        var library = new LibraryService(repository, new FileBlobStore(profile), TimeProvider.System);
        await library.InitializeAsync(TestContext.Current.CancellationToken);
        var included = await library.CreateAsync(new CreateItemRequest(
            "Incluido", "https://example.com/included", null,
            Tags: ["Delta"], Collections: ["Pruebas"]), TestContext.Current.CancellationToken);
        _ = await library.CreateAsync(new CreateItemRequest(
            "Excluido", "https://example.com/excluded", null,
            Tags: ["Otro"], Collections: ["Archivo"]), TestContext.Current.CancellationToken);
        var package = System.IO.Path.Combine(temporary.Path, "delta.zip");
        var service = new PortableBackupService(profile);

        var summary = await service.ExportDeltaAsync(
            package,
            new HashSet<string> { included.Id.ToString("D") },
            new HashSet<string>(),
            new HashSet<string>(),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, summary.ItemCount);
        Assert.Equal(1, summary.TagCount);
        Assert.Equal(1, summary.CollectionCount);
        Assert.Equal(summary, await service.InspectAsync(package, TestContext.Current.CancellationToken));
        using var archive = ZipFile.OpenRead(package);
        using var itemsReader = new StreamReader(archive.GetEntry("metadata/items.ndjson")!.Open());
        Assert.Contains(included.Id.ToString("D"), await itemsReader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<Guid> ReadDeviceIdAsync(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = 'device_id';";
        return Guid.Parse((string)(await command.ExecuteScalarAsync())!);
    }
}
