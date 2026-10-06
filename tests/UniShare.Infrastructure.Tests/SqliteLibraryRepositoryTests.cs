using Microsoft.Data.Sqlite;
using UniShare.Application;
using UniShare.Domain;
using UniShare.Infrastructure;

namespace UniShare.Infrastructure.Tests;

public sealed class SqliteLibraryRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreatePersistsAcrossRepositoryRestartAndWritesOperationAtomically()
    {
        using var temporary = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(temporary.Path, "library.db");
        var repository = new SqliteLibraryRepository(databasePath);
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(ItemKind.Link, "Documento", "https://example.com/doc", Now);

        await repository.CreateAsync(item, asset: null, cancellationToken: TestContext.Current.CancellationToken);

        var reopened = new SqliteLibraryRepository(databasePath);
        await reopened.InitializeAsync(TestContext.Current.CancellationToken);
        var loaded = await reopened.GetAsync(item.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(loaded);
        Assert.Equal(item, loaded);

        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MIN(origin_counter), MAX(origin_counter) FROM operations;";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt64(1));
        Assert.Equal(1, reader.GetInt64(2));
    }

    [Fact]
    public async Task SearchTreatsWildcardCharactersLiterally()
    {
        using var temporary = new TemporaryDirectory();
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(temporary.Path, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        await repository.CreateAsync(LibraryItem.Create(ItemKind.Link, "100% local", "https://example.com/local", Now), null, TestContext.Current.CancellationToken);
        await repository.CreateAsync(LibraryItem.Create(ItemKind.Link, "Otra entrada", "https://example.com/other", Now), null, TestContext.Current.CancellationToken);

        var results = await repository.SearchAsync("100%", cancellationToken: TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.Equal("100% local", result.Title);
    }

    [Fact]
    public async Task ArchiveAndRestoreArePersistentAndAppendOperations()
    {
        using var temporary = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(temporary.Path, "library.db");
        var repository = new SqliteLibraryRepository(databasePath);
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(ItemKind.Link, "Entrada", "https://example.com", Now);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);

        await repository.SetDeletedAsync(item.Id, true, Now.AddMinutes(1), TestContext.Current.CancellationToken);
        Assert.Empty(await repository.SearchAsync(null, cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(Assert.Single(await repository.SearchAsync(null, includeDeleted: true, cancellationToken: TestContext.Current.CancellationToken)).IsDeleted);

        await repository.SetDeletedAsync(item.Id, false, Now.AddMinutes(2), TestContext.Current.CancellationToken);
        Assert.False(Assert.Single(await repository.SearchAsync(null, cancellationToken: TestContext.Current.CancellationToken)).IsDeleted);

        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM operations;";
        Assert.Equal(3L, (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task CreateRejectsFileItemWithoutAssetWithoutWritingRows()
    {
        using var temporary = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(temporary.Path, "library.db");
        var repository = new SqliteLibraryRepository(databasePath);
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(ItemKind.File, "Archivo", null, Now);

        await Assert.ThrowsAsync<ArgumentException>(() => repository.CreateAsync(item, null, TestContext.Current.CancellationToken));

        Assert.Empty(await repository.SearchAsync(null, includeDeleted: true, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InitializeCreatesSchemaThatPassesSqliteIntegrityCheck()
    {
        using var temporary = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(temporary.Path, "library.db");
        var repository = new SqliteLibraryRepository(databasePath);

        await repository.InitializeAsync(TestContext.Current.CancellationToken);

        await using var connection = new SqliteConnection($"Data Source={databasePath};Foreign Keys=True");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check;";
        Assert.Equal("ok", await integrity.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        await using var version = connection.CreateCommand();
        version.CommandText = "SELECT version FROM schema_info;";
        Assert.Equal(4L, await version.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceOrganizationNormalizesMembershipsPreservesTombstonesAndIsSearchable()
    {
        using var temporary = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(temporary.Path, "library.db");
        var repository = new SqliteLibraryRepository(databasePath);
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(ItemKind.Link, "Lectura", "https://example.com/read", Now);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);

        await repository.ReplaceTagsAsync(item.Id, [" Ciencia  de   datos ", "CIENCIA DE DATOS", "Pendiente"], Now.AddMinutes(1), TestContext.Current.CancellationToken);
        await repository.ReplaceCollectionsAsync(item.Id, ["Universidad"], Now.AddMinutes(2), TestContext.Current.CancellationToken);

        var organization = await repository.GetOrganizationAsync(item.Id, TestContext.Current.CancellationToken);
        Assert.Equal(["Ciencia de datos", "Pendiente"], organization.Tags.Select(tag => tag.Name));
        Assert.Equal("Universidad", Assert.Single(organization.Collections).Name);
        Assert.Equal(item.Id, Assert.Single(await repository.SearchAsync("ciencia", cancellationToken: TestContext.Current.CancellationToken)).Id);
        Assert.Equal(item.Id, Assert.Single(await repository.SearchAsync("universidad", cancellationToken: TestContext.Current.CancellationToken)).Id);
        Assert.Equal(item.Id, Assert.Single(await repository.SearchAdvancedAsync(new ItemSearchOptions(Collection: " universidad "), TestContext.Current.CancellationToken)).Id);
        var collection = Assert.Single(await repository.ListCollectionsAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Universidad", collection.Name);
        Assert.Equal(1, collection.ItemCount);

        await repository.ReplaceTagsAsync(item.Id, ["Nueva"], Now.AddMinutes(3), TestContext.Current.CancellationToken);
        var replaced = await repository.GetOrganizationAsync(item.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Nueva", Assert.Single(replaced.Tags).Name);

        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM item_tags WHERE removed_at IS NOT NULL;";
        Assert.Equal(2L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        command.CommandText = "SELECT COUNT(*) FROM operations;";
        Assert.Equal(4L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReplaceTagsForMissingItemDoesNotCreateLabelsOrOperations()
    {
        using var temporary = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(temporary.Path, "library.db");
        var repository = new SqliteLibraryRepository(databasePath);
        await repository.InitializeAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            repository.ReplaceTagsAsync(Guid.NewGuid(), ["Huérfana"], Now, TestContext.Current.CancellationToken));

        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM tags) + (SELECT COUNT(*) FROM operations);";
        Assert.Equal(0L, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InitializeMigratesVersionOneDataAndBuildsFtsIndex()
    {
        using var temporary = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(temporary.Path, "library.db");
        var repository = new SqliteLibraryRepository(databasePath);
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(
            ItemKind.Link,
            "Neurociencia aplicada",
            "https://example.com/neuro",
            Now,
            description: "Memoria y aprendizaje");
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);

        SqliteConnection.ClearAllPools();
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var downgrade = connection.CreateCommand();
            downgrade.CommandText = """
                DROP TRIGGER items_fts_after_insert;
                DROP TRIGGER items_fts_after_delete;
                DROP TRIGGER items_fts_after_update;
                DROP TABLE items_fts;
                UPDATE schema_info SET version = 1;
                """;
            await downgrade.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var migrated = new SqliteLibraryRepository(databasePath);
        await migrated.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(item.Id, Assert.Single(await migrated.SearchAsync("neuroc", cancellationToken: TestContext.Current.CancellationToken)).Id);
        Assert.Equal(item.Id, Assert.Single(await migrated.SearchAsync("aprend", cancellationToken: TestContext.Current.CancellationToken)).Id);
    }

    [Fact]
    public async Task FtsIndexTracksMetadataUpdatesAndPrefixSearch()
    {
        using var temporary = new TemporaryDirectory();
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(temporary.Path, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var item = LibraryItem.Create(ItemKind.Link, "Borrador antiguo", "https://example.com", Now);
        await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);

        var updated = item.UpdateMetadata(
            "Arquitectura distribuida", "Curso", "Ada", "Sistemas locales", false, Now.AddMinutes(1));
        await repository.UpdateAsync(updated, TestContext.Current.CancellationToken);

        Assert.Empty(await repository.SearchAsync("borrador", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(item.Id, Assert.Single(await repository.SearchAsync("arquitect", cancellationToken: TestContext.Current.CancellationToken)).Id);
        Assert.Equal(item.Id, Assert.Single(await repository.SearchAsync("sistem loc", cancellationToken: TestContext.Current.CancellationToken)).Id);
    }

    [Fact]
    public async Task InitializeRejectsFutureSchemaVersionClearly()
    {
        using var temporary = new TemporaryDirectory();
        var databasePath = System.IO.Path.Combine(temporary.Path, "library.db");
        var repository = new SqliteLibraryRepository(databasePath);
        await repository.InitializeAsync(TestContext.Current.CancellationToken);

        SqliteConnection.ClearAllPools();
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE schema_info SET version = 999;";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => repository.InitializeAsync(TestContext.Current.CancellationToken));
        Assert.Contains("versión futura", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SearchCombinesKindFavoriteDateAndLocalAssetFilters()
    {
        using var temporary = new TemporaryDirectory();
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(temporary.Path, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var link = LibraryItem.Create(
            ItemKind.Link, "Favorito remoto", "https://example.com/favorite", Now.AddDays(-2), favorite: true);
        var file = LibraryItem.Create(ItemKind.File, "Documento local", null, Now);
        var hybrid = LibraryItem.Create(
            ItemKind.Hybrid, "Paper local", "https://example.com/paper", Now.AddDays(2));
        await repository.CreateAsync(link, null, TestContext.Current.CancellationToken);
        await repository.CreateAsync(file, CreateAsset(file.Id, "a"), TestContext.Current.CancellationToken);
        await repository.CreateAsync(hybrid, CreateAsset(hybrid.Id, "b"), TestContext.Current.CancellationToken);

        Assert.Equal(link.Id, Assert.Single(await repository.SearchAdvancedAsync(new ItemSearchOptions(FavoriteOnly: true), TestContext.Current.CancellationToken)).Id);
        Assert.Equal(2, (await repository.SearchAdvancedAsync(new ItemSearchOptions(HasLocalAsset: true), TestContext.Current.CancellationToken)).Count);
        Assert.Equal(2, (await repository.SearchAdvancedAsync(new ItemSearchOptions(
            Kinds: new HashSet<ItemKind> { ItemKind.File, ItemKind.Hybrid }), TestContext.Current.CancellationToken)).Count);
        Assert.Equal(hybrid.Id, Assert.Single(await repository.SearchAdvancedAsync(new ItemSearchOptions(
            Query: "paper",
            Kinds: new HashSet<ItemKind> { ItemKind.Hybrid },
            HasLocalAsset: true,
            CreatedFrom: Now.AddDays(1),
            CreatedTo: Now.AddDays(3)), TestContext.Current.CancellationToken)).Id);
    }

    [Fact]
    public async Task SearchPaginatesAndCanSelectOnlyDeletedItems()
    {
        using var temporary = new TemporaryDirectory();
        var repository = new SqliteLibraryRepository(System.IO.Path.Combine(temporary.Path, "library.db"));
        await repository.InitializeAsync(TestContext.Current.CancellationToken);
        var items = Enumerable.Range(0, 5)
            .Select(index => LibraryItem.Create(
                ItemKind.Link, $"Elemento {index}", $"https://example.com/{index}", Now.AddMinutes(index)))
            .ToArray();
        foreach (var item in items)
        {
            await repository.CreateAsync(item, null, TestContext.Current.CancellationToken);
        }
        await repository.SetDeletedAsync(items[2].Id, true, Now.AddHours(1), TestContext.Current.CancellationToken);

        var first = await repository.SearchAdvancedAsync(new ItemSearchOptions(Limit: 2), TestContext.Current.CancellationToken);
        var second = await repository.SearchAdvancedAsync(new ItemSearchOptions(Limit: 2, Offset: 2), TestContext.Current.CancellationToken);
        var deleted = await repository.SearchAdvancedAsync(new ItemSearchOptions(
            IncludeDeleted: true, DeletedOnly: true), TestContext.Current.CancellationToken);

        Assert.Equal(2, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Empty(first.Select(item => item.Id).Intersect(second.Select(item => item.Id)));
        Assert.Equal(items[2].Id, Assert.Single(deleted).Id);
    }

    private static AssetManifest CreateAsset(Guid itemId, string seed) => new(
        Guid.NewGuid(),
        itemId,
        new string(seed[0], 64),
        1,
        "application/octet-stream",
        $"{seed}.bin",
        $"blobs/{seed}/{seed}/{new string(seed[0], 64)}",
        Now);
}
