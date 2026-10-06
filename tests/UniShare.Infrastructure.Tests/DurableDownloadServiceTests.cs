using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using UniShare.Application;
using UniShare.Domain;
using UniShare.Infrastructure;

namespace UniShare.Infrastructure.Tests;

public sealed class DurableDownloadServiceTests
{
    [Fact]
    public async Task FetchMetadataReadsHtmlWithoutCreatingAnAsset()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var html = "<html><head><title>Artículo real</title><meta property=\"og:site_name\" content=\"Revista\"><meta name=\"author\" content=\"Ada\"><meta name=\"description\" content=\"Texto promocional\"></head><body><article><p>El artículo analiza cómo conservar una biblioteca personal sin depender de servicios externos. También explica por qué las copias locales facilitan la recuperación de los datos.</p></article></body></html>";
        var handler = new DelegateHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(html, Encoding.UTF8, "text/html"),
            };
            return response;
        });
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), handler: handler);

        var metadata = await service.FetchMetadataAsync(new Uri("https://example.com/article"), TestContext.Current.CancellationToken);

        Assert.NotNull(metadata);
        Assert.Equal("Artículo real", metadata.Title);
        Assert.Equal("Revista", metadata.Source);
        Assert.Equal("Ada", metadata.Author);
        Assert.Equal(
            "El artículo analiza cómo conservar una biblioteca personal sin depender de servicios externos. " +
            "También explica por qué las copias locales facilitan la recuperación de los datos.",
            metadata.Description);
        Assert.Equal("Texto promocional", metadata.PromotionalDescription);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(profile, "staging")));
    }

    [Fact]
    public async Task FetchMetadataPrefersOriginSocialMetadataAndFallsBackAcrossStandards()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        const string html = """
            <html><head><title>Título del documento</title>
            <meta name="description" content="Descripción genérica">
            <meta name="twitter:title" content="Título Twitter">
            <meta name="twitter:description" content="Descripción Twitter">
            <meta property="og:title" content="Título de origen">
            <meta property="og:description" content="Descripción de origen">
            <meta name="application-name" content="Aplicación">
            <meta property="article:author" content="Autora original"></head><body><main>
            <p>El contenido real estudia la conservación privada de enlaces y documentos en varios dispositivos. Expone las decisiones necesarias para mantenerlos accesibles sin una nube obligatoria.</p>
            </main></body></html>
            """;
        var handler = new DelegateHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(html, Encoding.UTF8, "text/html"),
            };
            return response;
        });
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), handler: handler);

        var metadata = await service.FetchMetadataAsync(
            new Uri("https://example.com/article"), TestContext.Current.CancellationToken);

        Assert.NotNull(metadata);
        Assert.Equal("Título de origen", metadata.Title);
        Assert.Equal("Aplicación", metadata.Source);
        Assert.Equal("Autora original", metadata.Author);
        Assert.Equal(
            "El contenido real estudia la conservación privada de enlaces y documentos en varios dispositivos. " +
            "Expone las decisiones necesarias para mantenerlos accesibles sin una nube obligatoria.",
            metadata.Description);
        Assert.Equal("Descripción de origen", metadata.PromotionalDescription);
        Assert.Equal(metadata.Description, metadata.ResolveDescription("Descripción de origen"));
        Assert.Equal("Mis notas", metadata.ResolveDescription("Mis notas"));
    }

    [Fact]
    public async Task FetchMetadataPrefersStructuredArticleBodyAndIgnoresNavigation()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        const string html = """
            <html><head><title>Informe</title><meta name="description" content="Compra ahora"></head><body>
            <nav><p>Portada Artículos Suscríbete Política de privacidad y otras secciones del sitio.</p></nav>
            <script type="application/ld+json">
            {"@context":"https://schema.org","@type":"Article","articleBody":"La investigación compara tres métodos de almacenamiento local y mide su resistencia ante fallos. Los resultados muestran que las copias verificadas por hash reducen los errores silenciosos."}
            </script>
            <main><p>Este texto alternativo no debe ganar cuando el autor publica articleBody de forma explícita y válida.</p></main>
            </body></html>
            """;
        var handler = new DelegateHandler((request, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(html, Encoding.UTF8, "text/html"),
        });
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), handler: handler);

        var metadata = await service.FetchMetadataAsync(
            new Uri("https://example.com/report"), TestContext.Current.CancellationToken);

        Assert.NotNull(metadata);
        Assert.Equal(
            "La investigación compara tres métodos de almacenamiento local y mide su resistencia ante fallos. " +
            "Los resultados muestran que las copias verificadas por hash reducen los errores silenciosos.",
            metadata.Description);
        Assert.Equal("Compra ahora", metadata.PromotionalDescription);
    }

    [Fact]
    public async Task FetchMetadataNeverUsesPromotionalCopyAsTheContentSummary()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        const string html = """
            <html><head><title>Producto</title>
            <meta property="og:description" content="La mejor oferta del año: compra hoy"></head>
            <body><main><p>Inicio</p><p>Comprar</p></main></body></html>
            """;
        var handler = new DelegateHandler((request, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(html, Encoding.UTF8, "text/html"),
        });
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), handler: handler);

        var metadata = await service.FetchMetadataAsync(
            new Uri("https://example.com/product"), TestContext.Current.CancellationToken);

        Assert.NotNull(metadata);
        Assert.Null(metadata.Description);
        Assert.Equal("La mejor oferta del año: compra hoy", metadata.PromotionalDescription);
        Assert.Null(metadata.ResolveDescription("La mejor oferta del año: compra hoy"));
    }

    [Fact]
    public async Task FetchMetadataSelectsTheMostSubstantiveSemanticRegion()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        const string html = """
            <html><head><title>Investigación</title></head><body>
            <article><p>Una tarjeta secundaria anuncia otra noticia y contiene suficiente texto para parecer un resumen válido.</p></article>
            <main>
              <p>La investigación principal documenta cómo se recuperaron los datos después de simular fallos en varios dispositivos. Detalla los resultados obtenidos y las comprobaciones realizadas sobre cada copia.</p>
              <p>El análisis concluye que la verificación periódica detecta daños antes de que afecten a la biblioteca personal.</p>
            </main>
            </body></html>
            """;
        var handler = new DelegateHandler((request, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(html, Encoding.UTF8, "text/html"),
        });
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), handler: handler);

        var metadata = await service.FetchMetadataAsync(
            new Uri("https://example.com/research"), TestContext.Current.CancellationToken);

        Assert.NotNull(metadata);
        Assert.StartsWith("La investigación principal documenta", metadata.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("tarjeta secundaria", metadata.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchMetadataUsesTheVideoDescriptionAndRejectsYouTubeNavigation()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        const string html = """
            <html><head><title>Vídeo útil - YouTube</title>
            <meta name="description" content="Descripción truncada"></head><body>
            <div>OverPersAuteursrechtContactCreatorsAdverterenOntwikkelaarsVoorwaardenPrivacyBeleid en veiligheidZo werkt YouTubeNieuwe functies testen</div>
            <script>var ytInitialPlayerResponse={"videoDetails":{"shortDescription":"Este vídeo explica cómo organizar una biblioteca personal y conservar cada elemento sin depender de una nube. También muestra el flujo completo de principio a fin."}};</script>
            </body></html>
            """;
        var handler = new DelegateHandler((request, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(html, Encoding.UTF8, "text/html"),
        });
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), handler: handler);

        var metadata = await service.FetchMetadataAsync(
            new Uri("https://www.youtube.com/watch?v=M7lc1UVf-VE"), TestContext.Current.CancellationToken);

        Assert.NotNull(metadata);
        Assert.StartsWith("Este vídeo explica", metadata.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("Auteursrecht", metadata.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(metadata.Description, metadata.ResolveDescription(
            "OverPersAuteursrechtContactCreatorsAdverterenOntwikkelaarsVoorwaardenPrivacyBeleid en " +
            "veiligheidZo werkt YouTubeNieuwe functies testen"));
        Assert.Equal("Mi nota personal", metadata.ResolveDescription("Mi nota personal"));
        Assert.Equal(
            "Developers must review copyright terms, privacy policy and contact us before publishing.",
            metadata.ResolveDescription(
                "Developers must review copyright terms, privacy policy and contact us before publishing."));
    }

    [Fact]
    public async Task FetchMetadataUsesTypedStructuredDescriptionWithoutAcceptingWholeBodyFallback()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        const string html = """
            <html><head><title>Grabación</title></head><body>
            <script type="application/ld+json">{"@type":"VideoObject","description":"La grabación documenta el proceso real de restauración y verifica el resultado con una copia local. Incluye los pasos necesarios para repetir la comprobación."}</script>
            <div>AboutPressCopyrightContact usCreatorsAdvertiseDevelopersTermsPrivacyPolicy</div>
            </body></html>
            """;
        var handler = new DelegateHandler((request, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(html, Encoding.UTF8, "text/html"),
        });
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), handler: handler);

        var metadata = await service.FetchMetadataAsync(
            new Uri("https://example.com/video"), TestContext.Current.CancellationToken);

        Assert.NotNull(metadata);
        Assert.StartsWith("La grabación documenta", metadata.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("Copyright", metadata.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchMetadataLeavesSummaryEmptyWhenBodyContainsOnlyUnmarkedNavigation()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        const string html = """
            <html><head><title>Página dinámica</title></head><body>
            <div>AboutPressCopyrightContact usCreatorsAdvertiseDevelopersTermsPrivacyPolicyHow YouTube worksTest new features</div>
            </body></html>
            """;
        var handler = new DelegateHandler((request, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent(html, Encoding.UTF8, "text/html"),
        });
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), handler: handler);

        var metadata = await service.FetchMetadataAsync(
            new Uri("https://example.com/dynamic"), TestContext.Current.CancellationToken);

        Assert.NotNull(metadata);
        Assert.Null(metadata.Description);
    }

    [Fact]
    public async Task RunStreamsToCasCompletesAtomicallyAndConvertsLinkToHybrid()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(
            ItemKind.Link, "Recurso", "https://example.com/file.pdf", FixedTimeProvider.Now);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);
        var bytes = Encoding.UTF8.GetBytes("downloaded-content");
        var handler = new DelegateHandler((request, _) =>
        {
            Assert.Null(request.Headers.Range);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(bytes),
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return response;
        });
        var policy = new RecordingEndpointPolicy();
        var blobStore = new FileBlobStore(profile);
        using var service = new DurableDownloadService(
            repository, blobStore, policy, profile, new FixedTimeProvider(), handler: handler);

        var queued = await service.QueueAsync(item.Id, new Uri(item.OriginalUrl!), TestContext.Current.CancellationToken);
        var completed = await service.RunAsync(queued.Id, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadJobState.Completed, completed.State);
        Assert.Equal(bytes.Length, completed.BytesReceived);
        Assert.NotNull(completed.AssetId);
        Assert.Single(policy.ValidatedUris);
        var asset = Assert.Single(await repository.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));
        Assert.Equal(completed.AssetId, asset.Id);
        Assert.Equal("application/pdf", asset.MimeType);
        Assert.True(await blobStore.VerifyAsync(asset.Sha256, TestContext.Current.CancellationToken));
        Assert.Equal(ItemKind.Hybrid, (await repository.GetAsync(item.Id, TestContext.Current.CancellationToken))!.Kind);
        Assert.Empty(Directory.EnumerateFiles(
            System.IO.Path.Combine(profile, "staging"), "*.partial", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task RunResumesWithRangeAndIfRangeWithoutDuplicatingBytes()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(
            ItemKind.Link, "Parcial", "https://example.com/resume.bin", FixedTimeProvider.Now);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);
        var queued = await repository.QueueDownloadAsync(
            item.Id, new Uri(item.OriginalUrl!), FixedTimeProvider.Now, CancellationToken.None);
        var partialPath = System.IO.Path.Combine(profile, "staging", $"download-{queued.Id:N}.partial");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(partialPath, "abc"u8.ToArray(), TestContext.Current.CancellationToken);
        await repository.UpdateDownloadAsync(
            queued.Id, DownloadJobState.Paused, 3, 6, "\"v1\"", null,
            "cancelled", "paused", FixedTimeProvider.Now, CancellationToken.None);
        var handler = new DelegateHandler((request, _) =>
        {
            Assert.Equal(3, request.Headers.Range!.Ranges.Single().From);
            Assert.Equal("\"v1\"", request.Headers.IfRange!.EntityTag!.Tag);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                RequestMessage = request,
                Content = new ByteArrayContent("def"u8.ToArray()),
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 5, 6);
            return response;
        });
        var blobStore = new FileBlobStore(profile);
        using var service = new DurableDownloadService(
            repository, blobStore, new RecordingEndpointPolicy(), profile, new FixedTimeProvider(), handler: handler);

        var completed = await service.RunAsync(queued.Id, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadJobState.Completed, completed.State);
        var asset = Assert.Single(await repository.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData("abcdef"u8.ToArray())).ToLowerInvariant(),
            asset.Sha256);
    }

    [Fact]
    public async Task RunRejectsResumeWhenEtagChangedAndDeletesMixedPartial()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(
            ItemKind.Link, "Cambió", "https://example.com/changed.bin", FixedTimeProvider.Now);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);
        var queued = await repository.QueueDownloadAsync(
            item.Id, new Uri(item.OriginalUrl!), FixedTimeProvider.Now, CancellationToken.None);
        var partialPath = System.IO.Path.Combine(profile, "staging", $"download-{queued.Id:N}.partial");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(partialPath, "abc"u8.ToArray(), TestContext.Current.CancellationToken);
        await repository.UpdateDownloadAsync(
            queued.Id, DownloadJobState.Paused, 3, 6, "\"v1\"", null,
            "cancelled", "paused", FixedTimeProvider.Now, CancellationToken.None);
        var handler = new DelegateHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                RequestMessage = request,
                Content = new ByteArrayContent("def"u8.ToArray()),
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"v2\"");
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(3, 5, 6);
            return response;
        });
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), handler: handler);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.RunAsync(queued.Id, TestContext.Current.CancellationToken));

        Assert.False(File.Exists(partialPath));
        Assert.Equal(DownloadJobState.Failed, (await repository.GetDownloadAsync(queued.Id, TestContext.Current.CancellationToken))!.State);
        Assert.Empty(await repository.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunRejectsOversizedResponseAndNeverCreatesAsset()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(
            ItemKind.Link, "Grande", "https://example.com/large.bin", FixedTimeProvider.Now);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);
        var handler = new DelegateHandler((request, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(new byte[20]),
        });
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), maximumBytes: 10, handler: handler);
        var job = await service.QueueAsync(item.Id, new Uri(item.OriginalUrl!), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.RunAsync(job.Id, TestContext.Current.CancellationToken));

        Assert.Equal(DownloadJobState.Failed, (await repository.GetDownloadAsync(job.Id, TestContext.Current.CancellationToken))!.State);
        Assert.Empty(await repository.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));
        Assert.Equal(ItemKind.Link, (await repository.GetAsync(item.Id, TestContext.Current.CancellationToken))!.Kind);
    }

    [Fact]
    public async Task RunUsesContentSignatureBeforeUntrustedHeaderAndExtension()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(
            ItemKind.Link, "Tipo", "https://example.com/not-an-image.png", FixedTimeProvider.Now);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);
        var handler = new DelegateHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent("%PDF-1.7\ncontent"u8.ToArray()),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return response;
        });
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), handler: handler);
        var job = await service.QueueAsync(item.Id, new Uri(item.OriginalUrl!), TestContext.Current.CancellationToken);

        await service.RunAsync(job.Id, TestContext.Current.CancellationToken);

        Assert.Equal("application/pdf", Assert.Single(await repository.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken)).MimeType);
    }

    [Fact]
    public async Task RunRewritesHtmlAsStaticOfflineSnapshot()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(
            ItemKind.Link, "Página", "https://example.com/article", FixedTimeProvider.Now);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);
        const string hostileHtml = """
            <!doctype html><html><head><title>Article &amp; Notes</title>
            <meta name="author" content="Ada Lovelace">
            <meta content="A useful summary" name="description">
            <meta property="og:site_name" content="Example Journal">
            <script>fetch('https://tracker.invalid/steal')</script></head>
            <body onclick="steal()"><h1>Useful heading</h1><article><p>Readable content explains the stored element directly and preserves the essential facts. It remains available in the inert offline copy without external scripts.</p></article>
            <img src="https://tracker.invalid/pixel"></body></html>
            """;
        var handler = new DelegateHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(hostileHtml)),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return response;
        });
        var blobStore = new FileBlobStore(profile);
        using var service = new DurableDownloadService(
            repository, blobStore, new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), handler: handler);
        var job = await service.QueueAsync(item.Id, new Uri(item.OriginalUrl!), TestContext.Current.CancellationToken);

        await service.RunAsync(job.Id, TestContext.Current.CancellationToken);

        var asset = Assert.Single(await repository.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));
        var snapshot = await File.ReadAllTextAsync(blobStore.GetAbsolutePath(asset.RelativePath), TestContext.Current.CancellationToken);
        Assert.Equal("text/html", asset.MimeType);
        Assert.EndsWith(".offline.html", asset.OriginalName, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'", snapshot, StringComparison.Ordinal);
        Assert.Contains("Article &amp; Notes", snapshot, StringComparison.Ordinal);
        Assert.Contains("Useful heading", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", snapshot, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick=", snapshot, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tracker.invalid", snapshot, StringComparison.OrdinalIgnoreCase);
        Assert.True(await blobStore.VerifyAsync(asset.Sha256, TestContext.Current.CancellationToken));
        var enriched = await repository.GetAsync(item.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Página", enriched!.Title);
        Assert.Equal("Example Journal", enriched.Source);
        Assert.Equal("Ada Lovelace", enriched.Author);
        Assert.Equal(
            "Readable content explains the stored element directly and preserves the essential facts. " +
            "It remains available in the inert offline copy without external scripts.",
            enriched.Description);
    }

    [Theory]
    [InlineData("https://example.com/deceptive.html", "image/png", "<body onload=\"steal()\">Safe words</body>")]
    [InlineData("https://example.com/deceptive.svg", "image/svg+xml", "<svg onload=\"steal()\"><text>Safe words</text></svg>")]
    public async Task RunNeverPublishesActiveWebFormatsRaw(
        string url,
        string declaredType,
        string payload)
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(ItemKind.Link, "Sospechoso", url, FixedTimeProvider.Now);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);
        var handler = new DelegateHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(payload)),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(declaredType);
            return response;
        });
        var blobStore = new FileBlobStore(profile);
        using var service = new DurableDownloadService(
            repository, blobStore, new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), handler: handler);
        var job = await service.QueueAsync(item.Id, new Uri(url), TestContext.Current.CancellationToken);

        await service.RunAsync(job.Id, TestContext.Current.CancellationToken);

        var asset = Assert.Single(await repository.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));
        var snapshot = await File.ReadAllTextAsync(blobStore.GetAbsolutePath(asset.RelativePath), TestContext.Current.CancellationToken);
        Assert.Equal("text/html", asset.MimeType);
        Assert.EndsWith(".offline.html", asset.OriginalName, StringComparison.Ordinal);
        Assert.Contains("Safe words", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("onload=", snapshot, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("default-src 'none'", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnrichmentPreservesUserEditedMetadata()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(
            ItemKind.Link, "Mi título", "https://example.com/article", FixedTimeProvider.Now,
            source: "Mi fuente", author: "Mi autor", description: "Mis notas");
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);
        const string html = """
            <html><head><title>Remote title</title><meta name="author" content="Remote author">
            <meta name="description" content="Remote description"></head><body>Text</body></html>
            """;
        var handler = new DelegateHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(html)),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return response;
        });
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), handler: handler);
        var job = await service.QueueAsync(item.Id, new Uri(item.OriginalUrl!), TestContext.Current.CancellationToken);

        await service.RunAsync(job.Id, TestContext.Current.CancellationToken);

        var preserved = await repository.GetAsync(item.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Mi título", preserved!.Title);
        Assert.Equal("Mi fuente", preserved.Source);
        Assert.Equal("Mi autor", preserved.Author);
        Assert.Equal("Mis notas", preserved.Description);
    }

    [Fact]
    public async Task EnrichmentReplacesOnlyAutomaticallyGeneratedTitle()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var uri = new Uri("https://www.example.com/article");
        var item = LibraryItem.Create(
            ItemKind.Link, LinkCaptureDefaults.TitleFromUrl(uri), uri.ToString(), FixedTimeProvider.Now);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);
        const string html = """
            <html><head><title>Título detectado</title></head><body>Texto seguro</body></html>
            """;
        var handler = new DelegateHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(html)),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return response;
        });
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), handler: handler);
        var job = await service.QueueAsync(item.Id, uri, TestContext.Current.CancellationToken);

        await service.RunAsync(job.Id, TestContext.Current.CancellationToken);

        var enriched = await repository.GetAsync(item.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Título detectado", enriched!.Title);
    }

    [Fact]
    public async Task RunRejectsTruncatedBodyAndKeepsItemWithoutAsset()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(
            ItemKind.Link, "Truncado", "https://example.com/truncated.bin", FixedTimeProvider.Now);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);
        var handler = new DelegateHandler((request, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent("abc"u8.ToArray()),
            };
            response.Content.Headers.ContentLength = 10;
            return response;
        });
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), handler: handler);
        var job = await service.QueueAsync(item.Id, new Uri(item.OriginalUrl!), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.RunAsync(job.Id, TestContext.Current.CancellationToken));

        Assert.Equal(DownloadJobState.Failed, (await repository.GetDownloadAsync(job.Id, TestContext.Current.CancellationToken))!.State);
        Assert.Empty(await repository.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunValidatesEveryRedirectDestination()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(
            ItemKind.Link, "Redirect", "https://example.com/start", FixedTimeProvider.Now);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);
        var calls = 0;
        var handler = new DelegateHandler((request, _) =>
        {
            calls++;
            if (calls == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found) { RequestMessage = request };
                redirect.Headers.Location = new Uri("https://cdn.example.com/final.bin");
                return redirect;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent("ok"u8.ToArray()),
            };
        });
        var policy = new RecordingEndpointPolicy();
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), policy, profile, new FixedTimeProvider(), handler: handler);
        var job = await service.QueueAsync(item.Id, new Uri(item.OriginalUrl!), TestContext.Current.CancellationToken);

        await service.RunAsync(job.Id, TestContext.Current.CancellationToken);

        Assert.Equal(2, policy.ValidatedUris.Count);
        Assert.Equal("example.com", policy.ValidatedUris[0].Host);
        Assert.Equal("cdn.example.com", policy.ValidatedUris[1].Host);
    }

    [Fact]
    public async Task CancelDeletesPartialFileAndPersistsTerminalState()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(
            ItemKind.Link, "Cancelar", "https://example.com/cancel.bin", FixedTimeProvider.Now);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), handler: new DelegateHandler((_, _) =>
                throw new InvalidOperationException("No debe iniciar una petición HTTP.")));
        var job = await service.QueueAsync(item.Id, new Uri(item.OriginalUrl!), TestContext.Current.CancellationToken);
        var partialPath = System.IO.Path.Combine(profile, "staging", $"download-{job.Id:N}.partial");
        await File.WriteAllBytesAsync(partialPath, "partial"u8.ToArray(), TestContext.Current.CancellationToken);
        await repository.UpdateDownloadAsync(
            job.Id, DownloadJobState.Paused, 7, 100, "\"v1\"", null,
            "cancelled", "paused", FixedTimeProvider.Now, CancellationToken.None);

        var cancelled = await service.CancelAsync(job.Id, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadJobState.Cancelled, cancelled.State);
        Assert.Equal("user_cancelled", cancelled.ErrorCode);
        Assert.False(cancelled.CanRun);
        Assert.False(File.Exists(partialPath));
        Assert.Empty(await repository.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task QueueSurvivesRepositoryReopen()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var databasePath = System.IO.Path.Combine(profile, "library.db");
        var repository = new SqliteLibraryRepository(databasePath);
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(
            ItemKind.Link, "Persistente", "https://example.com/persist.bin", FixedTimeProvider.Now);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);
        var queued = await repository.QueueDownloadAsync(
            item.Id, new Uri(item.OriginalUrl!), FixedTimeProvider.Now, CancellationToken.None);

        var reopened = new SqliteLibraryRepository(databasePath);
        await reopened.InitializeAsync(TestContext.Current.CancellationToken);
        var persisted = await reopened.GetDownloadAsync(queued.Id, TestContext.Current.CancellationToken);

        Assert.NotNull(persisted);
        Assert.Equal(queued, persisted);
        Assert.Equal(queued.Id, Assert.Single(await reopened.ListDownloadsAsync(TestContext.Current.CancellationToken)).Id);
    }

    [Fact]
    public async Task InterruptedRunningJobCanResumeAfterRepositoryReopen()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var databasePath = System.IO.Path.Combine(profile, "library.db");
        var repository = new SqliteLibraryRepository(databasePath);
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(
            ItemKind.Link, "Interrumpido", "https://example.com/interrupted.bin", FixedTimeProvider.Now);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);
        var queued = await repository.QueueDownloadAsync(
            item.Id, new Uri(item.OriginalUrl!), FixedTimeProvider.Now, CancellationToken.None);
        await repository.UpdateDownloadAsync(
            queued.Id, DownloadJobState.Running, 0, null, null, null,
            null, null, FixedTimeProvider.Now, CancellationToken.None);
        var reopened = new SqliteLibraryRepository(databasePath);
        await reopened.InitializeAsync(TestContext.Current.CancellationToken);
        var handler = new DelegateHandler((request, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new ByteArrayContent("recovered"u8.ToArray()),
        });
        using var service = new DurableDownloadService(
            reopened, new FileBlobStore(profile), new RecordingEndpointPolicy(), profile,
            new FixedTimeProvider(), handler: handler);

        var resumed = await service.RunAsync(queued.Id, TestContext.Current.CancellationToken);

        Assert.Equal(DownloadJobState.Completed, resumed.State);
        Assert.Single(await reopened.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DefaultTransportConnectsOnlyToAddressReturnedByPolicy()
    {
        using var temporary = new TemporaryDirectory();
        var profile = System.IO.Path.Combine(temporary.Path, "profile");
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(profile, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var source = new Uri($"http://must-not-resolve.invalid:{endpoint.Port}/pinned.bin");
        var item = LibraryItem.Create(ItemKind.Link, "Fijada", source.AbsoluteUri, FixedTimeProvider.Now);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);
        var policy = new FixedAddressPolicy(IPAddress.Loopback);
        using var service = new DurableDownloadService(
            repository, new FileBlobStore(profile), policy, profile, new FixedTimeProvider());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = ServeSingleResponseAsync(listener, timeout.Token);

        try
        {
            var job = await service.QueueAsync(item.Id, source, TestContext.Current.CancellationToken);
            var completed = await service.RunAsync(job.Id, timeout.Token);
            await server;

            Assert.Equal(DownloadJobState.Completed, completed.State);
            Assert.True(policy.ValidatedUris.Count >= 2);
            Assert.All(policy.ValidatedUris, uri => Assert.Equal(source, uri));
            Assert.Single(await repository.GetAssetsAsync(item.Id, TestContext.Current.CancellationToken));
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task ServeSingleResponseAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(
            stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
        {
        }

        var response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Length: 6\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n\r\npinned");
        await stream.WriteAsync(response, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(callback(request, cancellationToken));
    }

    private sealed class RecordingEndpointPolicy : IRemoteEndpointPolicy
    {
        public List<Uri> ValidatedUris { get; } = [];

        public Task<IReadOnlyList<IPAddress>> ResolveAndValidateAsync(
            Uri uri,
            CancellationToken cancellationToken = default)
        {
            ValidatedUris.Add(uri);
            return Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("93.184.216.34")]);
        }
    }

    private sealed class FixedAddressPolicy(IPAddress address) : IRemoteEndpointPolicy
    {
        public List<Uri> ValidatedUris { get; } = [];

        public Task<IReadOnlyList<IPAddress>> ResolveAndValidateAsync(
            Uri uri,
            CancellationToken cancellationToken = default)
        {
            ValidatedUris.Add(uri);
            return Task.FromResult<IReadOnlyList<IPAddress>>([address]);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
