using System.Net;
using System.Text;
using UniShare.Application;
using UniShare.Infrastructure;

namespace UniShare.Infrastructure.Tests;

public sealed class OfflineWorkflowTests
{
    [Fact]
    public async Task CoreWorkflowPersistsSearchesOpensAndRestoresWithNetworkUnavailable()
    {
        using var temporary = new TemporaryDirectory();
        var profile = Path.Combine(temporary.Path, "offline-profile");
        var source = Path.Combine(temporary.Path, "offline-document.pdf");
        var sourceBytes = Encoding.ASCII.GetBytes("%PDF-1.7 UniShare offline fixture");
        await File.WriteAllBytesAsync(source, sourceBytes, TestContext.Current.CancellationToken);
        var repository = new SqliteLibraryRepository(Path.Combine(profile, "library.db"));
        var blobStore = new FileBlobStore(profile);
        var library = new LibraryService(repository, blobStore, TimeProvider.System);
        var endpointPolicy = new RejectNetworkPolicy();
        var httpHandler = new RejectNetworkHandler();
        using var downloads = new DurableDownloadService(
            repository, blobStore, endpointPolicy, profile, TimeProvider.System, handler: httpHandler);
        await library.InitializeAsync(TestContext.Current.CancellationToken);

        var created = await library.CreateAsync(new CreateItemRequest(
            "Manual sin conexión",
            "https://example.com/manual",
            source,
            Source: "Archivo local",
            Author: "Equipo UniShare",
            Description: "Documento disponible sin conexión",
            Favorite: true,
            Tags: ["Offline"],
            Collections: ["Manuales"]), TestContext.Current.CancellationToken);
        Assert.Empty(await downloads.ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(created.Id, Assert.Single(await library.SearchAsync(
            "disponible", cancellationToken: TestContext.Current.CancellationToken)).Id);
        var asset = Assert.Single(await library.GetAssetsAsync(
            created.Id, TestContext.Current.CancellationToken));
        Assert.Equal(
            sourceBytes,
            await File.ReadAllBytesAsync(
                blobStore.GetAbsolutePath(asset.RelativePath), TestContext.Current.CancellationToken));

        var backupPath = Path.Combine(temporary.Path, "offline-backup.unishare.zip");
        var backup = new PortableBackupService(profile);
        var exported = await backup.ExportAsync(backupPath, TestContext.Current.CancellationToken);
        var reopenedRepository = new SqliteLibraryRepository(Path.Combine(profile, "library.db"));
        await reopenedRepository.InitializeAsync(TestContext.Current.CancellationToken);
        var persisted = Assert.Single(await reopenedRepository.SearchAsync(
            "manual", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(created, persisted);

        var restoredProfile = Path.Combine(temporary.Path, "restored-offline-profile");
        var restored = await backup.RestoreToNewProfileAsync(
            backupPath, restoredProfile, TestContext.Current.CancellationToken);
        Assert.Equal(exported, restored);
        var restoredRepository = new SqliteLibraryRepository(Path.Combine(restoredProfile, "library.db"));
        await restoredRepository.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(created, Assert.Single(await restoredRepository.SearchAsync(
            "offline", cancellationToken: TestContext.Current.CancellationToken)));
        var restoredAsset = Assert.Single(await restoredRepository.GetAssetsAsync(
            created.Id, TestContext.Current.CancellationToken));
        Assert.True(await new FileBlobStore(restoredProfile).VerifyAsync(
            restoredAsset.Sha256, TestContext.Current.CancellationToken));
        Assert.Equal(0, endpointPolicy.Attempts);
        Assert.Equal(0, httpHandler.Attempts);
    }

    private sealed class RejectNetworkPolicy : IRemoteEndpointPolicy
    {
        public int Attempts { get; private set; }

        public Task<IReadOnlyList<IPAddress>> ResolveAndValidateAsync(
            Uri uri,
            CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new HttpRequestException("La red está deshabilitada durante esta prueba.");
        }
    }

    private sealed class RejectNetworkHandler : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;
            throw new HttpRequestException("La red está deshabilitada durante esta prueba.");
        }
    }
}
