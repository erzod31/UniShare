using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using UniShare.Application;
using UniShare.Domain;

namespace UniShare.Infrastructure;

public sealed class SqliteLibraryRepository(string databasePath) : IItemRepository, IDownloadJobRepository
{
    private const int SchemaVersion = 4;
    private readonly string _databasePath = Path.GetFullPath(databasePath);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath) ?? throw new InvalidOperationException(
            "La base de datos necesita un directorio padre."));

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = FULL;

            CREATE TABLE IF NOT EXISTS schema_info (
                version INTEGER NOT NULL
            );

            INSERT INTO schema_info(version)
            SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM schema_info);

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS items (
                id TEXT PRIMARY KEY,
                kind INTEGER NOT NULL CHECK(kind IN (1, 2, 3)),
                title TEXT NOT NULL CHECK(length(title) BETWEEN 1 AND 500),
                original_url TEXT NULL,
                canonical_url TEXT NULL,
                source TEXT NULL,
                author TEXT NULL,
                description TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                deleted_at TEXT NULL,
                favorite INTEGER NOT NULL CHECK(favorite IN (0, 1)),
                CHECK((kind = 2 AND original_url IS NULL) OR (kind IN (1, 3) AND original_url IS NOT NULL))
            );

            CREATE INDEX IF NOT EXISTS ix_items_updated_at ON items(updated_at DESC);
            CREATE INDEX IF NOT EXISTS ix_items_canonical_url ON items(canonical_url);
            CREATE INDEX IF NOT EXISTS ix_items_deleted_at ON items(deleted_at);

            CREATE TABLE IF NOT EXISTS assets (
                id TEXT PRIMARY KEY,
                item_id TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE,
                sha256 TEXT NOT NULL CHECK(length(sha256) = 64),
                byte_length INTEGER NOT NULL CHECK(byte_length >= 0),
                mime_type TEXT NOT NULL,
                original_name TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_assets_item_id ON assets(item_id);
            CREATE INDEX IF NOT EXISTS ix_assets_sha256 ON assets(sha256);

            CREATE TABLE IF NOT EXISTS operations (
                operation_id TEXT PRIMARY KEY,
                origin_device_id TEXT NOT NULL,
                origin_counter INTEGER NOT NULL CHECK(origin_counter > 0),
                entity_id TEXT NOT NULL,
                entity_type TEXT NOT NULL,
                operation_type TEXT NOT NULL,
                schema_version INTEGER NOT NULL,
                payload TEXT NOT NULL,
                causal_context TEXT NOT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(origin_device_id, origin_counter)
            );

            CREATE INDEX IF NOT EXISTS ix_operations_entity ON operations(entity_id, origin_counter);

            CREATE TABLE IF NOT EXISTS tags (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                normalized_name TEXT NOT NULL UNIQUE
            );

            CREATE TABLE IF NOT EXISTS item_tags (
                membership_id TEXT PRIMARY KEY,
                item_id TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE,
                tag_id TEXT NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
                removed_at TEXT NULL,
                UNIQUE(item_id, tag_id, membership_id)
            );

            CREATE TABLE IF NOT EXISTS collections (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                normalized_name TEXT NOT NULL UNIQUE
            );

            CREATE TABLE IF NOT EXISTS item_collections (
                membership_id TEXT PRIMARY KEY,
                item_id TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE,
                collection_id TEXT NOT NULL REFERENCES collections(id) ON DELETE CASCADE,
                removed_at TEXT NULL,
                UNIQUE(item_id, collection_id, membership_id)
            );

            CREATE TABLE IF NOT EXISTS conflicts (
                id TEXT PRIMARY KEY,
                entity_id TEXT NOT NULL,
                field_name TEXT NOT NULL,
                versions_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                resolved_at TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await EnsureSettingsAsync(connection, cancellationToken);
        await MigrateAsync(connection, cancellationToken);
        await ValidateSchemaVersionAsync(connection, cancellationToken);
    }

    public async Task CreateAsync(
        LibraryItem item,
        AssetManifest? asset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (asset is not null && asset.ItemId != item.Id)
        {
            throw new ArgumentException("El asset no pertenece al item.", nameof(asset));
        }

        if (item.Kind is ItemKind.File or ItemKind.Hybrid && asset is null)
        {
            throw new ArgumentException("Los items de archivo o híbridos necesitan un asset.", nameof(asset));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await InsertItemAsync(connection, transaction, item, cancellationToken);

        if (asset is not null)
        {
            await InsertAssetAsync(connection, transaction, asset, cancellationToken);
        }

        await WriteOperationAsync(connection, transaction, item, "item.created", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<LibraryItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM items WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadItem(reader) : null;
    }

    public async Task<IReadOnlyList<LibraryItem>> GetAllAsync(
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = includeDeleted
            ? "SELECT * FROM items ORDER BY updated_at DESC, id;"
            : "SELECT * FROM items WHERE deleted_at IS NULL ORDER BY updated_at DESC, id;";
        var results = new List<LibraryItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadItem(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<LibraryItem>> SearchAsync(
        string? query,
        bool includeDeleted = false,
        int limit = 200,
        CancellationToken cancellationToken = default) =>
        await SearchAdvancedAsync(new ItemSearchOptions(Query: query, IncludeDeleted: includeDeleted, Limit: limit), cancellationToken);

    public async Task<IReadOnlyList<LibraryItem>> SearchAdvancedAsync(
        ItemSearchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "El límite debe estar entre 1 y 500.");
        }

        if (options.CreatedFrom > options.CreatedTo)
        {
            throw new ArgumentException("La fecha inicial no puede ser posterior a la final.", nameof(options));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var clauses = new List<string>();

        if (options.DeletedOnly)
        {
            clauses.Add("deleted_at IS NOT NULL");
        }
        else if (!options.IncludeDeleted)
        {
            clauses.Add("deleted_at IS NULL");
        }

        if (!string.IsNullOrWhiteSpace(options.Query))
        {
            var ftsQuery = BuildFtsQuery(options.Query);
            var textualClause = ftsQuery is null
                ? """
                    (title LIKE $query ESCAPE '\' COLLATE NOCASE OR
                     original_url LIKE $query ESCAPE '\' COLLATE NOCASE OR
                     source LIKE $query ESCAPE '\' COLLATE NOCASE OR
                     author LIKE $query ESCAPE '\' COLLATE NOCASE OR
                     description LIKE $query ESCAPE '\' COLLATE NOCASE)
                    """
                : "items.rowid IN (SELECT rowid FROM items_fts WHERE items_fts MATCH $fts)";
            clauses.Add($"""
                ({textualClause} OR
                 EXISTS (
                     SELECT 1 FROM item_tags it
                     INNER JOIN tags t ON t.id = it.tag_id
                     WHERE it.item_id = items.id AND it.removed_at IS NULL
                       AND t.name LIKE $query ESCAPE '\' COLLATE NOCASE
                 ) OR
                 EXISTS (
                     SELECT 1 FROM item_collections ic
                     INNER JOIN collections c ON c.id = ic.collection_id
                     WHERE ic.item_id = items.id AND ic.removed_at IS NULL
                       AND c.name LIKE $query ESCAPE '\' COLLATE NOCASE
                 ))
                """);
            command.Parameters.AddWithValue("$query", $"%{EscapeLike(options.Query.Trim())}%");
            if (ftsQuery is not null)
            {
                command.Parameters.AddWithValue("$fts", ftsQuery);
            }
        }

        if (options.Kinds is { Count: > 0 })
        {
            var parameters = new List<string>(options.Kinds.Count);
            var index = 0;
            foreach (var kind in options.Kinds.OrderBy(value => value))
            {
                if (!Enum.IsDefined(kind))
                {
                    throw new ArgumentOutOfRangeException(nameof(options), "El filtro contiene un tipo desconocido.");
                }

                var parameter = $"$kind{index++}";
                parameters.Add(parameter);
                command.Parameters.AddWithValue(parameter, (int)kind);
            }

            clauses.Add($"kind IN ({string.Join(", ", parameters)})");
        }

        if (options.FavoriteOnly)
        {
            clauses.Add("favorite = 1");
        }

        if (options.HasLocalAsset.HasValue)
        {
            clauses.Add(options.HasLocalAsset.Value
                ? "EXISTS (SELECT 1 FROM assets a WHERE a.item_id = items.id)"
                : "NOT EXISTS (SELECT 1 FROM assets a WHERE a.item_id = items.id)");
        }

        if (options.CreatedFrom.HasValue)
        {
            clauses.Add("created_at >= $created_from");
            command.Parameters.AddWithValue("$created_from", FormatTimestamp(options.CreatedFrom.Value));
        }

        if (options.CreatedTo.HasValue)
        {
            clauses.Add("created_at <= $created_to");
            command.Parameters.AddWithValue("$created_to", FormatTimestamp(options.CreatedTo.Value));
        }
        if (options.Offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "El desplazamiento no puede ser negativo.");
        }

        if (!string.IsNullOrWhiteSpace(options.Collection))
        {
            var normalized = LibraryCollectionDefinition.Normalize(options.Collection);
            clauses.Add("""
                EXISTS (
                    SELECT 1 FROM item_collections ic
                    INNER JOIN collections c ON c.id = ic.collection_id
                    WHERE ic.item_id = items.id AND ic.removed_at IS NULL
                      AND c.normalized_name = $collection
                )
                """);
            command.Parameters.AddWithValue("$collection", normalized.NormalizedName);
        }

        command.CommandText = $"SELECT * FROM items{(clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses))} ORDER BY updated_at DESC, id LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$limit", options.Limit);
        command.Parameters.AddWithValue("$offset", options.Offset);
        var results = new List<LibraryItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadItem(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<CollectionSummary>> ListCollectionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.name, COUNT(DISTINCT ic.item_id)
            FROM collections c
            INNER JOIN item_collections ic ON ic.collection_id = c.id AND ic.removed_at IS NULL
            INNER JOIN items i ON i.id = ic.item_id AND i.deleted_at IS NULL
            GROUP BY c.id, c.name, c.normalized_name
            HAVING COUNT(DISTINCT ic.item_id) > 0
            ORDER BY c.normalized_name;
            """;
        var result = new List<CollectionSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CollectionSummary(reader.GetString(0), reader.GetInt32(1)));
        }
        return result;
    }

    public async Task UpdateAsync(LibraryItem item, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE items
            SET title = $title, source = $source, author = $author, description = $description,
                updated_at = $updated_at, deleted_at = $deleted_at, favorite = $favorite
            WHERE id = $id;
            """;
        AddItemUpdateParameters(command, item);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken);
        if (changed != 1)
        {
            throw new KeyNotFoundException("El item ya no existe.");
        }

        await WriteOperationAsync(connection, transaction, item, "item.updated", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetDeletedAsync(
        Guid id,
        bool deleted,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var current = await GetAsync(id, cancellationToken) ??
            throw new KeyNotFoundException("El item ya no existe.");
        var changed = deleted ? current.Archive(now) : current.Restore(now);
        await UpdateWithOperationAsync(changed, deleted ? "item.deleted" : "item.restored", cancellationToken);
    }

    public async Task<IReadOnlyList<AssetManifest>> GetAssetsAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM assets WHERE item_id = $item_id ORDER BY created_at, id;";
        command.Parameters.AddWithValue("$item_id", itemId.ToString("D"));
        var results = new List<AssetManifest>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new AssetManifest(
                Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                Guid.Parse(reader.GetString(reader.GetOrdinal("item_id"))),
                reader.GetString(reader.GetOrdinal("sha256")),
                reader.GetInt64(reader.GetOrdinal("byte_length")),
                reader.GetString(reader.GetOrdinal("mime_type")),
                reader.GetString(reader.GetOrdinal("original_name")),
                reader.GetString(reader.GetOrdinal("relative_path")),
                ParseTimestamp(reader.GetString(reader.GetOrdinal("created_at")))));
        }

        return results;
    }

    public async Task<ItemOrganization> GetOrganizationAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureItemExistsAsync(connection, transaction: null, itemId, cancellationToken);

        var tags = new List<LibraryTag>();
        await using (var tagCommand = connection.CreateCommand())
        {
            tagCommand.CommandText = """
                SELECT DISTINCT t.id, t.name, t.normalized_name
                FROM tags t
                INNER JOIN item_tags it ON it.tag_id = t.id
                WHERE it.item_id = $item_id AND it.removed_at IS NULL
                ORDER BY t.normalized_name;
                """;
            tagCommand.Parameters.AddWithValue("$item_id", itemId.ToString("D"));
            await using var reader = await tagCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tags.Add(new LibraryTag(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2)));
            }
        }

        var collections = new List<LibraryCollectionDefinition>();
        await using (var collectionCommand = connection.CreateCommand())
        {
            collectionCommand.CommandText = """
                SELECT DISTINCT c.id, c.name, c.normalized_name
                FROM collections c
                INNER JOIN item_collections ic ON ic.collection_id = c.id
                WHERE ic.item_id = $item_id AND ic.removed_at IS NULL
                ORDER BY c.normalized_name;
                """;
            collectionCommand.Parameters.AddWithValue("$item_id", itemId.ToString("D"));
            await using var reader = await collectionCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                collections.Add(new LibraryCollectionDefinition(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2)));
            }
        }

        return new ItemOrganization(tags, collections);
    }

    public Task ReplaceTagsAsync(
        Guid itemId,
        IReadOnlyCollection<string> names,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);
        var normalized = NormalizeNames(names, LibraryTag.Normalize);
        return ReplaceMembershipsAsync(itemId, normalized, MembershipKind.Tag, now, cancellationToken);
    }

    public Task ReplaceCollectionsAsync(
        Guid itemId,
        IReadOnlyCollection<string> names,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);
        var normalized = NormalizeNames(names, LibraryCollectionDefinition.Normalize);
        return ReplaceMembershipsAsync(itemId, normalized, MembershipKind.Collection, now, cancellationToken);
    }

    public async Task<DownloadJob> QueueDownloadAsync(
        Guid itemId,
        Uri sourceUri,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceUri);
        _ = UrlCanonicalizer.ParseHttpUrl(sourceUri.AbsoluteUri);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureItemExistsAsync(connection, transaction: null, itemId, cancellationToken);
        var job = new DownloadJob(
            Guid.NewGuid(), itemId, sourceUri, DownloadJobState.Queued, 0, null, null, null,
            null, null, null, now.ToUniversalTime(), now.ToUniversalTime());
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO download_jobs(
                id, item_id, source_url, state, bytes_received, total_bytes, etag, last_modified,
                asset_id, error_code, error_message, created_at, updated_at)
            VALUES($id, $item_id, $source_url, $state, 0, NULL, NULL, NULL, NULL, NULL, NULL, $created_at, $updated_at);
            """;
        command.Parameters.AddWithValue("$id", job.Id.ToString("D"));
        command.Parameters.AddWithValue("$item_id", job.ItemId.ToString("D"));
        command.Parameters.AddWithValue("$source_url", job.SourceUri.AbsoluteUri);
        command.Parameters.AddWithValue("$state", (int)job.State);
        command.Parameters.AddWithValue("$created_at", FormatTimestamp(job.CreatedAt));
        command.Parameters.AddWithValue("$updated_at", FormatTimestamp(job.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return job;
    }

    public async Task<DownloadJob?> GetDownloadAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM download_jobs WHERE id = $id;";
        command.Parameters.AddWithValue("$id", jobId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDownload(reader) : null;
    }

    public async Task<IReadOnlyList<DownloadJob>> ListDownloadsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM download_jobs ORDER BY updated_at DESC, id;";
        var jobs = new List<DownloadJob>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(ReadDownload(reader));
        }

        return jobs;
    }

    public async Task UpdateDownloadAsync(
        Guid jobId,
        DownloadJobState state,
        long bytesReceived,
        long? totalBytes,
        string? etag,
        DateTimeOffset? lastModified,
        string? errorCode,
        string? errorMessage,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytesReceived);
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE download_jobs
            SET state = $state, bytes_received = $bytes_received, total_bytes = $total_bytes,
                etag = $etag, last_modified = $last_modified, error_code = $error_code,
                error_message = $error_message, updated_at = $updated_at
            WHERE id = $id AND state <> $completed;
            """;
        command.Parameters.AddWithValue("$id", jobId.ToString("D"));
        command.Parameters.AddWithValue("$state", (int)state);
        command.Parameters.AddWithValue("$bytes_received", bytesReceived);
        command.Parameters.AddWithValue("$total_bytes", totalBytes.HasValue ? totalBytes.Value : DBNull.Value);
        command.Parameters.AddWithValue("$etag", (object?)etag ?? DBNull.Value);
        command.Parameters.AddWithValue("$last_modified", lastModified.HasValue ? FormatTimestamp(lastModified.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$error_code", (object?)Truncate(errorCode, 100) ?? DBNull.Value);
        command.Parameters.AddWithValue("$error_message", (object?)Truncate(errorMessage, 1000) ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", FormatTimestamp(now));
        command.Parameters.AddWithValue("$completed", (int)DownloadJobState.Completed);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("La descarga no existe o ya está completada.");
        }
    }

    public async Task CompleteDownloadAsync(
        Guid jobId,
        AssetManifest asset,
        WebCaptureMetadata? metadata,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        Guid itemId;
        var replaceAutomaticTitle = false;
        await using (var find = connection.CreateCommand())
        {
            find.Transaction = transaction;
            find.CommandText = """
                SELECT j.item_id, j.source_url, i.title
                FROM download_jobs AS j
                INNER JOIN items AS i ON i.id = j.item_id
                WHERE j.id = $id AND j.state = $running;
                """;
            find.Parameters.AddWithValue("$id", jobId.ToString("D"));
            find.Parameters.AddWithValue("$running", (int)DownloadJobState.Running);
            await using var reader = await find.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException("La descarga no existe o ya no está en ejecución.");
            }

            itemId = Guid.Parse(reader.GetString(0));
            var sourceUri = new Uri(reader.GetString(1), UriKind.Absolute);
            replaceAutomaticTitle = string.Equals(
                reader.GetString(2), LinkCaptureDefaults.TitleFromUrl(sourceUri), StringComparison.Ordinal);
        }

        if (asset.ItemId != itemId)
        {
            throw new ArgumentException("El asset no pertenece al item de la descarga.", nameof(asset));
        }

        await InsertAssetAsync(connection, transaction, asset, cancellationToken);
        await using (var updateItem = connection.CreateCommand())
        {
            var remoteTitle = string.IsNullOrWhiteSpace(metadata?.Title)
                ? null
                : Truncate(metadata.Title.Trim(), 500);
            updateItem.Transaction = transaction;
            updateItem.CommandText = """
                UPDATE items
                SET kind = CASE WHEN kind = 1 THEN 3 ELSE kind END,
                    title = CASE
                        WHEN $replace_title = 1 AND $remote_title IS NOT NULL THEN $remote_title
                        ELSE title
                    END,
                    source = COALESCE(source, $source),
                    author = COALESCE(author, $author),
                    description = COALESCE(description, $description),
                    updated_at = $updated_at
                WHERE id = $item_id;
                """;
            updateItem.Parameters.AddWithValue("$item_id", itemId.ToString("D"));
            updateItem.Parameters.AddWithValue("$replace_title", replaceAutomaticTitle ? 1 : 0);
            updateItem.Parameters.AddWithValue("$remote_title", (object?)remoteTitle ?? DBNull.Value);
            updateItem.Parameters.AddWithValue("$source", (object?)metadata?.Source ?? DBNull.Value);
            updateItem.Parameters.AddWithValue("$author", (object?)metadata?.Author ?? DBNull.Value);
            updateItem.Parameters.AddWithValue("$description", (object?)metadata?.Description ?? DBNull.Value);
            updateItem.Parameters.AddWithValue("$updated_at", FormatTimestamp(now));
            await updateItem.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var updateJob = connection.CreateCommand())
        {
            updateJob.Transaction = transaction;
            updateJob.CommandText = """
                UPDATE download_jobs
                SET state = $state, bytes_received = $bytes, total_bytes = $bytes, asset_id = $asset_id,
                    error_code = NULL, error_message = NULL, updated_at = $updated_at
                WHERE id = $id;
                """;
            updateJob.Parameters.AddWithValue("$id", jobId.ToString("D"));
            updateJob.Parameters.AddWithValue("$state", (int)DownloadJobState.Completed);
            updateJob.Parameters.AddWithValue("$bytes", asset.ByteLength);
            updateJob.Parameters.AddWithValue("$asset_id", asset.Id.ToString("D"));
            updateJob.Parameters.AddWithValue("$updated_at", FormatTimestamp(now));
            await updateJob.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteRawOperationAsync(
            connection, transaction, itemId, "asset.downloaded",
            JsonSerializer.Serialize(new { Asset = asset, Metadata = metadata }), now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task UpdateWithOperationAsync(
        LibraryItem item,
        string operationType,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE items SET updated_at = $updated_at, deleted_at = $deleted_at WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", item.Id.ToString("D"));
        command.Parameters.AddWithValue("$updated_at", FormatTimestamp(item.UpdatedAt));
        command.Parameters.AddWithValue("$deleted_at", (object?)item.DeletedAt is null ? DBNull.Value : FormatTimestamp(item.DeletedAt.Value));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new KeyNotFoundException("El item ya no existe.");
        }

        await WriteOperationAsync(connection, transaction, item, operationType, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task ReplaceMembershipsAsync(
        Guid itemId,
        Dictionary<string, string> desiredNames,
        MembershipKind kind,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var configuration = kind switch
        {
            MembershipKind.Tag => new MembershipConfiguration(
                "tags", "item_tags", "tag_id", "item.tags.replaced"),
            MembershipKind.Collection => new MembershipConfiguration(
                "collections", "item_collections", "collection_id", "item.collections.replaced"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await EnsureItemExistsAsync(connection, transaction, itemId, cancellationToken);

        var activeMemberships = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = $"""
                SELECT l.normalized_name, m.membership_id
                FROM {configuration.LabelTable} l
                INNER JOIN {configuration.MembershipTable} m ON m.{configuration.ForeignKey} = l.id
                WHERE m.item_id = $item_id AND m.removed_at IS NULL;
                """;
            command.Parameters.AddWithValue("$item_id", itemId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var normalizedName = reader.GetString(0);
                if (!activeMemberships.TryGetValue(normalizedName, out var memberships))
                {
                    memberships = [];
                    activeMemberships.Add(normalizedName, memberships);
                }

                memberships.Add(reader.GetString(1));
            }
        }

        foreach (var (normalizedName, memberships) in activeMemberships)
        {
            if (desiredNames.ContainsKey(normalizedName))
            {
                continue;
            }

            foreach (var membershipId in memberships)
            {
                await using var remove = connection.CreateCommand();
                remove.Transaction = transaction;
                remove.CommandText = $"UPDATE {configuration.MembershipTable} SET removed_at = $removed_at WHERE membership_id = $membership_id AND removed_at IS NULL;";
                remove.Parameters.AddWithValue("$removed_at", FormatTimestamp(now));
                remove.Parameters.AddWithValue("$membership_id", membershipId);
                await remove.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        foreach (var (normalizedName, displayName) in desiredNames)
        {
            if (activeMemberships.ContainsKey(normalizedName))
            {
                continue;
            }

            var labelId = await GetOrCreateLabelAsync(
                connection, transaction, configuration.LabelTable, displayName, normalizedName, cancellationToken);
            await using var add = connection.CreateCommand();
            add.Transaction = transaction;
            add.CommandText = $"""
                INSERT INTO {configuration.MembershipTable}(membership_id, item_id, {configuration.ForeignKey}, removed_at)
                VALUES($membership_id, $item_id, $label_id, NULL);
                """;
            add.Parameters.AddWithValue("$membership_id", Guid.NewGuid().ToString("D"));
            add.Parameters.AddWithValue("$item_id", itemId.ToString("D"));
            add.Parameters.AddWithValue("$label_id", labelId.ToString("D"));
            await add.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var touch = connection.CreateCommand())
        {
            touch.Transaction = transaction;
            touch.CommandText = "UPDATE items SET updated_at = $updated_at WHERE id = $item_id;";
            touch.Parameters.AddWithValue("$updated_at", FormatTimestamp(now));
            touch.Parameters.AddWithValue("$item_id", itemId.ToString("D"));
            await touch.ExecuteNonQueryAsync(cancellationToken);
        }

        var payload = JsonSerializer.Serialize(new
        {
            Names = desiredNames.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToArray(),
        });
        await WriteRawOperationAsync(
            connection, transaction, itemId, configuration.OperationType, payload, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            DefaultTimeout = 5,
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task EnsureSettingsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO settings(key, value) VALUES ('device_id', $device_id);
            INSERT OR IGNORE INTO settings(key, value) VALUES ('next_counter', '0');
            """;
        command.Parameters.AddWithValue("$device_id", Guid.NewGuid().ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ValidateSchemaVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_info LIMIT 1;";
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (result != SchemaVersion)
        {
            throw new NotSupportedException($"Versión de base de datos no compatible: {result}.");
        }
    }

    private static async Task MigrateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var readVersion = connection.CreateCommand();
        readVersion.CommandText = "SELECT version FROM schema_info LIMIT 1;";
        var version = Convert.ToInt32(
            await readVersion.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);

        if (version > SchemaVersion)
        {
            throw new NotSupportedException($"La base de datos usa una versión futura ({version}).");
        }

        if (version < 2)
        {
            await using var transaction = connection.BeginTransaction();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                CREATE VIRTUAL TABLE IF NOT EXISTS items_fts USING fts5(
                    title,
                    original_url,
                    source,
                    author,
                    description,
                    content='items',
                    content_rowid='rowid',
                    tokenize='unicode61 remove_diacritics 2'
                );

                CREATE TRIGGER IF NOT EXISTS items_fts_after_insert AFTER INSERT ON items BEGIN
                    INSERT INTO items_fts(rowid, title, original_url, source, author, description)
                    VALUES (new.rowid, new.title, new.original_url, new.source, new.author, new.description);
                END;

                CREATE TRIGGER IF NOT EXISTS items_fts_after_delete AFTER DELETE ON items BEGIN
                    INSERT INTO items_fts(items_fts, rowid, title, original_url, source, author, description)
                    VALUES ('delete', old.rowid, old.title, old.original_url, old.source, old.author, old.description);
                END;

                CREATE TRIGGER IF NOT EXISTS items_fts_after_update AFTER UPDATE ON items BEGIN
                    INSERT INTO items_fts(items_fts, rowid, title, original_url, source, author, description)
                    VALUES ('delete', old.rowid, old.title, old.original_url, old.source, old.author, old.description);
                    INSERT INTO items_fts(rowid, title, original_url, source, author, description)
                    VALUES (new.rowid, new.title, new.original_url, new.source, new.author, new.description);
                END;

                INSERT INTO items_fts(items_fts) VALUES('rebuild');

                UPDATE schema_info SET version = 2;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            version = 2;
        }

        if (version < 3)
        {
            await using var transaction = connection.BeginTransaction();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS download_jobs (
                    id TEXT PRIMARY KEY,
                    item_id TEXT NOT NULL REFERENCES items(id) ON DELETE CASCADE,
                    source_url TEXT NOT NULL,
                    state INTEGER NOT NULL CHECK(state BETWEEN 1 AND 6),
                    bytes_received INTEGER NOT NULL CHECK(bytes_received >= 0),
                    total_bytes INTEGER NULL CHECK(total_bytes IS NULL OR total_bytes >= 0),
                    etag TEXT NULL,
                    last_modified TEXT NULL,
                    asset_id TEXT NULL REFERENCES assets(id),
                    error_code TEXT NULL,
                    error_message TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS ix_download_jobs_state_updated ON download_jobs(state, updated_at DESC);
                CREATE INDEX IF NOT EXISTS ix_download_jobs_item ON download_jobs(item_id);
                UPDATE schema_info SET version = 3;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            version = 3;
        }

        if (version < 4)
        {
            await using var transaction = connection.BeginTransaction();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS sync_item_state (
                    peer_device_id TEXT NOT NULL,
                    item_id TEXT NOT NULL,
                    local_hash TEXT NOT NULL CHECK(length(local_hash) = 64),
                    remote_hash TEXT NOT NULL CHECK(length(remote_hash) = 64),
                    last_synced_at TEXT NOT NULL,
                    PRIMARY KEY(peer_device_id, item_id)
                );

                CREATE INDEX IF NOT EXISTS ix_conflicts_unresolved
                    ON conflicts(resolved_at, entity_id);
                UPDATE schema_info SET version = 4;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task EnsureItemExistsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid itemId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM items WHERE id = $item_id;";
        command.Parameters.AddWithValue("$item_id", itemId.ToString("D"));
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            throw new KeyNotFoundException("El item ya no existe.");
        }
    }

    private static async Task<Guid> GetOrCreateLabelAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string displayName,
        string normalizedName,
        CancellationToken cancellationToken)
    {
        var candidateId = Guid.NewGuid();
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = $"INSERT OR IGNORE INTO {table}(id, name, normalized_name) VALUES($id, $name, $normalized_name);";
            insert.Parameters.AddWithValue("$id", candidateId.ToString("D"));
            insert.Parameters.AddWithValue("$name", displayName);
            insert.Parameters.AddWithValue("$normalized_name", normalizedName);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = $"SELECT id FROM {table} WHERE normalized_name = $normalized_name;";
        select.Parameters.AddWithValue("$normalized_name", normalizedName);
        var result = await select.ExecuteScalarAsync(cancellationToken) ??
            throw new InvalidOperationException("No se pudo crear la etiqueta o colección.");
        return Guid.Parse((string)result);
    }

    private static async Task InsertItemAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LibraryItem item,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO items(
                id, kind, title, original_url, canonical_url, source, author, description,
                created_at, updated_at, deleted_at, favorite)
            VALUES(
                $id, $kind, $title, $original_url, $canonical_url, $source, $author, $description,
                $created_at, $updated_at, $deleted_at, $favorite);
            """;
        AddItemParameters(command, item);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAssetAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetManifest asset,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO assets(id, item_id, sha256, byte_length, mime_type, original_name, relative_path, created_at)
            VALUES($id, $item_id, $sha256, $byte_length, $mime_type, $original_name, $relative_path, $created_at);
            """;
        command.Parameters.AddWithValue("$id", asset.Id.ToString("D"));
        command.Parameters.AddWithValue("$item_id", asset.ItemId.ToString("D"));
        command.Parameters.AddWithValue("$sha256", asset.Sha256);
        command.Parameters.AddWithValue("$byte_length", asset.ByteLength);
        command.Parameters.AddWithValue("$mime_type", asset.MimeType);
        command.Parameters.AddWithValue("$original_name", asset.OriginalName);
        command.Parameters.AddWithValue("$relative_path", asset.RelativePath);
        command.Parameters.AddWithValue("$created_at", FormatTimestamp(asset.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteOperationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LibraryItem item,
        string operationType,
        CancellationToken cancellationToken)
    {
        await WriteRawOperationAsync(
            connection,
            transaction,
            item.Id,
            operationType,
            JsonSerializer.Serialize(item),
            item.UpdatedAt,
            cancellationToken);
    }

    private static async Task WriteRawOperationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid entityId,
        string operationType,
        string payload,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var (deviceId, counter) = await NextOperationIdentityAsync(connection, transaction, cancellationToken);
        var operation = new DomainOperation(
            Guid.NewGuid(),
            deviceId,
            counter,
            entityId,
            "item",
            operationType,
            SchemaVersion,
            payload,
            "{}",
            createdAt);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO operations(
                operation_id, origin_device_id, origin_counter, entity_id, entity_type,
                operation_type, schema_version, payload, causal_context, created_at)
            VALUES(
                $operation_id, $device_id, $counter, $entity_id, $entity_type,
                $operation_type, $schema_version, $payload, $causal_context, $created_at);
            """;
        command.Parameters.AddWithValue("$operation_id", operation.OperationId.ToString("D"));
        command.Parameters.AddWithValue("$device_id", operation.OriginDeviceId.ToString("D"));
        command.Parameters.AddWithValue("$counter", operation.OriginCounter);
        command.Parameters.AddWithValue("$entity_id", operation.EntityId.ToString("D"));
        command.Parameters.AddWithValue("$entity_type", operation.EntityType);
        command.Parameters.AddWithValue("$operation_type", operation.OperationType);
        command.Parameters.AddWithValue("$schema_version", operation.SchemaVersion);
        command.Parameters.AddWithValue("$payload", operation.Payload);
        command.Parameters.AddWithValue("$causal_context", operation.CausalContext);
        command.Parameters.AddWithValue("$created_at", FormatTimestamp(operation.CreatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(Guid DeviceId, long Counter)> NextOperationIdentityAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE settings SET value = CAST(value AS INTEGER) + 1 WHERE key = 'next_counter';";
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("No se pudo avanzar el contador local.");
        }

        await using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT
                (SELECT value FROM settings WHERE key = 'device_id'),
                (SELECT value FROM settings WHERE key = 'next_counter');
            """;
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Falta la identidad local.");
        }

        return (Guid.Parse(reader.GetString(0)), long.Parse(reader.GetString(1), CultureInfo.InvariantCulture));
    }

    private static void AddItemParameters(SqliteCommand command, LibraryItem item)
    {
        command.Parameters.AddWithValue("$id", item.Id.ToString("D"));
        command.Parameters.AddWithValue("$kind", (int)item.Kind);
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$original_url", (object?)item.OriginalUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$canonical_url", (object?)item.CanonicalUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$source", (object?)item.Source ?? DBNull.Value);
        command.Parameters.AddWithValue("$author", (object?)item.Author ?? DBNull.Value);
        command.Parameters.AddWithValue("$description", (object?)item.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", FormatTimestamp(item.CreatedAt));
        command.Parameters.AddWithValue("$updated_at", FormatTimestamp(item.UpdatedAt));
        command.Parameters.AddWithValue("$deleted_at", item.DeletedAt.HasValue ? FormatTimestamp(item.DeletedAt.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$favorite", item.Favorite ? 1 : 0);
    }

    private static void AddItemUpdateParameters(SqliteCommand command, LibraryItem item)
    {
        command.Parameters.AddWithValue("$id", item.Id.ToString("D"));
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$source", (object?)item.Source ?? DBNull.Value);
        command.Parameters.AddWithValue("$author", (object?)item.Author ?? DBNull.Value);
        command.Parameters.AddWithValue("$description", (object?)item.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", FormatTimestamp(item.UpdatedAt));
        command.Parameters.AddWithValue("$deleted_at", item.DeletedAt.HasValue ? FormatTimestamp(item.DeletedAt.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$favorite", item.Favorite ? 1 : 0);
    }

    private static LibraryItem ReadItem(SqliteDataReader reader)
    {
        string? NullableString(string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        var deleted = NullableString("deleted_at");
        return LibraryItem.Rehydrate(
            Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            (ItemKind)reader.GetInt32(reader.GetOrdinal("kind")),
            reader.GetString(reader.GetOrdinal("title")),
            NullableString("original_url"),
            NullableString("canonical_url"),
            NullableString("source"),
            NullableString("author"),
            NullableString("description"),
            ParseTimestamp(reader.GetString(reader.GetOrdinal("created_at"))),
            ParseTimestamp(reader.GetString(reader.GetOrdinal("updated_at"))),
            deleted is null ? null : ParseTimestamp(deleted),
            reader.GetInt32(reader.GetOrdinal("favorite")) == 1);
    }

    private static DownloadJob ReadDownload(SqliteDataReader reader)
    {
        string? NullableString(string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        long? NullableInt64(string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
        }

        var lastModified = NullableString("last_modified");
        var assetId = NullableString("asset_id");
        return new DownloadJob(
            Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            Guid.Parse(reader.GetString(reader.GetOrdinal("item_id"))),
            new Uri(reader.GetString(reader.GetOrdinal("source_url")), UriKind.Absolute),
            (DownloadJobState)reader.GetInt32(reader.GetOrdinal("state")),
            reader.GetInt64(reader.GetOrdinal("bytes_received")),
            NullableInt64("total_bytes"),
            NullableString("etag"),
            lastModified is null ? null : ParseTimestamp(lastModified),
            assetId is null ? null : Guid.Parse(assetId),
            NullableString("error_code"),
            NullableString("error_message"),
            ParseTimestamp(reader.GetString(reader.GetOrdinal("created_at"))),
            ParseTimestamp(reader.GetString(reader.GetOrdinal("updated_at"))));
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static string? Truncate(string? value, int maximumLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maximumLength ? value : value[..maximumLength];

    private static string? BuildFtsQuery(string value)
    {
        var tokens = Regex.Matches(value.Normalize(), @"[\p{L}\p{N}]+")
            .Select(match => match.Value.Replace("\"", "\"\"", StringComparison.Ordinal))
            .Where(token => token.Length > 0)
            .Select(token => $"\"{token}\"*")
            .ToArray();
        return tokens.Length == 0 ? null : string.Join(" AND ", tokens);
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static Dictionary<string, string> NormalizeNames(
        IEnumerable<string> names,
        Func<string, (string Name, string NormalizedName)> normalizer)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in names)
        {
            var (name, normalizedName) = normalizer(value);
            result.TryAdd(normalizedName, name);
        }

        return result;
    }

    private enum MembershipKind
    {
        Tag,
        Collection,
    }

    private sealed record MembershipConfiguration(
        string LabelTable,
        string MembershipTable,
        string ForeignKey,
        string OperationType);
}

