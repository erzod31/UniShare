using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using UniShare.Application;
using UniShare.Domain;
using static UniShare.Infrastructure.PortablePackageContract;

namespace UniShare.Infrastructure;

public sealed class PortableSyncService : ISyncService
{
    private readonly string _profileRoot;
    private readonly string _databasePath;
    private readonly IBackupService _backupService;
    private readonly IBlobStore _blobStore;

    public PortableSyncService(
        string profileRoot,
        IBackupService backupService,
        IBlobStore blobStore)
    {
        _profileRoot = Path.GetFullPath(profileRoot);
        _databasePath = Path.Combine(_profileRoot, "library.db");
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _blobStore = blobStore ?? throw new ArgumentNullException(nameof(blobStore));
    }

    public async Task<SyncImportSummary> ImportAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        _ = await _backupService.InspectAsync(packagePath, cancellationToken);
        var snapshot = await ReadPackageAsync(packagePath, cancellationToken);
        ValidatePackageRelations(snapshot.Items, snapshot.Assets);
        if (snapshot.Manifest.SourceDeviceId is not Guid sourceDeviceId)
        {
            throw new InvalidDataException(
                "El paquete no incluye identidad de origen. Expórtalo de nuevo con UniShare 0.2 o posterior.");
        }

        var localDeviceId = await ReadLocalDeviceIdAsync(cancellationToken);
        if (sourceDeviceId == localDeviceId)
        {
            throw new InvalidOperationException("No se puede sincronizar un perfil consigo mismo.");
        }

        var importedAssets = await ImportBlobsAsync(packagePath, snapshot.Assets, cancellationToken);
        var imported = 0;
        var updated = 0;
        var unchanged = 0;
        var conflicts = 0;
        var now = DateTimeOffset.UtcNow;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        foreach (var rawRemote in snapshot.Items)
        {
            var remote = NormalizeAndValidate(rawRemote);
            var remoteHash = HashItem(remote);
            var local = await ReadItemAsync(connection, transaction, remote.Id, cancellationToken);
            if (local is null)
            {
                await InsertItemAsync(connection, transaction, remote, cancellationToken);
                await UpsertStateAsync(connection, transaction, sourceDeviceId, remote.Id,
                    remoteHash, remoteHash, now, cancellationToken);
                await WriteOperationAsync(connection, transaction, remote, "item.synced", now, cancellationToken);
                imported++;
                continue;
            }

            var localHash = HashItem(local);
            if (string.Equals(localHash, remoteHash, StringComparison.Ordinal))
            {
                await UpsertStateAsync(connection, transaction, sourceDeviceId, remote.Id,
                    localHash, remoteHash, now, cancellationToken);
                unchanged++;
                continue;
            }

            var unresolved = await HasUnresolvedConflictAsync(
                connection, transaction, sourceDeviceId, remote.Id, cancellationToken);
            var state = await ReadStateAsync(connection, transaction, sourceDeviceId, remote.Id, cancellationToken);
            if (!unresolved && state is not null)
            {
                var localChanged = !string.Equals(localHash, state.LocalHash, StringComparison.Ordinal);
                var remoteChanged = !string.Equals(remoteHash, state.RemoteHash, StringComparison.Ordinal);
                if (!localChanged && remoteChanged)
                {
                    await UpdateItemAsync(connection, transaction, remote, cancellationToken);
                    await UpsertStateAsync(connection, transaction, sourceDeviceId, remote.Id,
                        remoteHash, remoteHash, now, cancellationToken);
                    await WriteOperationAsync(connection, transaction, remote, "item.synced", now, cancellationToken);
                    updated++;
                    continue;
                }

                if (localChanged && !remoteChanged)
                {
                    await UpsertStateAsync(connection, transaction, sourceDeviceId, remote.Id,
                        localHash, remoteHash, now, cancellationToken);
                    unchanged++;
                    continue;
                }
            }

            await UpsertConflictAsync(
                connection, transaction, sourceDeviceId, local, localHash, remote, remoteHash, now, cancellationToken);
            conflicts++;
        }

        foreach (var asset in snapshot.Assets)
        {
            ValidateAsset(asset);
            await InsertAssetIfMissingAsync(connection, transaction, asset, cancellationToken);
        }

        var tagIds = await EnsureLabelsAsync(
            connection, transaction, "tags", snapshot.Tags, cancellationToken);
        var collectionIds = await EnsureLabelsAsync(
            connection, transaction, "collections", snapshot.Collections, cancellationToken);
        foreach (var membership in snapshot.TagMemberships)
        {
            await InsertMembershipIfMissingAsync(
                connection, transaction, "item_tags", "tag_id", membership,
                tagIds, cancellationToken);
        }
        foreach (var membership in snapshot.CollectionMemberships)
        {
            await InsertMembershipIfMissingAsync(
                connection, transaction, "item_collections", "collection_id", membership,
                collectionIds, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new SyncImportSummary(sourceDeviceId, imported, updated, unchanged, conflicts, importedAssets);
    }

    public async Task<IReadOnlyList<SyncConflict>> ListConflictsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, entity_id, versions_json, created_at
            FROM conflicts
            WHERE resolved_at IS NULL AND field_name LIKE 'sync:%'
            ORDER BY created_at DESC;
            """;
        var result = new List<SyncConflict>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var payload = JsonSerializer.Deserialize<ConflictPayload>(reader.GetString(2), JsonOptions) ??
                throw new InvalidDataException("Conflicto de sincronización dañado.");
            result.Add(new SyncConflict(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                payload.SourceDeviceId,
                payload.Local.Title,
                payload.Remote.Title,
                ParseTimestamp(reader.GetString(3))));
        }

        return result;
    }

    public async Task ResolveConflictAsync(
        Guid conflictId,
        SyncResolution resolution,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();
        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = """
            SELECT versions_json FROM conflicts
            WHERE id = $id AND resolved_at IS NULL AND field_name LIKE 'sync:%';
            """;
        read.Parameters.AddWithValue("$id", conflictId.ToString("D"));
        var serialized = (string?)await read.ExecuteScalarAsync(cancellationToken) ??
            throw new KeyNotFoundException("El conflicto no existe o ya está resuelto.");
        var payload = JsonSerializer.Deserialize<ConflictPayload>(serialized, JsonOptions) ??
            throw new InvalidDataException("Conflicto de sincronización dañado.");
        var chosen = resolution switch
        {
            SyncResolution.KeepLocal => NormalizeAndValidate(payload.Local),
            SyncResolution.UseRemote => NormalizeAndValidate(payload.Remote),
            _ => throw new ArgumentOutOfRangeException(nameof(resolution)),
        };
        if (resolution == SyncResolution.UseRemote)
        {
            await UpdateItemAsync(connection, transaction, chosen, cancellationToken);
            await WriteOperationAsync(
                connection, transaction, chosen, "item.conflict-resolved", DateTimeOffset.UtcNow, cancellationToken);
        }

        var chosenHash = HashItem(chosen);
        await UpsertStateAsync(
            connection, transaction, payload.SourceDeviceId, chosen.Id,
            chosenHash, payload.RemoteHash, DateTimeOffset.UtcNow, cancellationToken);
        await using var resolve = connection.CreateCommand();
        resolve.Transaction = transaction;
        resolve.CommandText = "UPDATE conflicts SET resolved_at = $resolved_at WHERE id = $id;";
        resolve.Parameters.AddWithValue("$id", conflictId.ToString("D"));
        resolve.Parameters.AddWithValue("$resolved_at", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await resolve.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<PackageSnapshot> ReadPackageAsync(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        var manifest = await ReadJsonAsync<BackupManifest>(archive, "manifest.json", cancellationToken);
        var items = await ReadNdjsonAsync<ItemRecord>(archive, "metadata/items.ndjson", cancellationToken);
        var assets = await ReadNdjsonAsync<AssetRecord>(archive, "metadata/assets.ndjson", cancellationToken);
        var tags = await ReadNdjsonAsync<LabelRecord>(archive, "metadata/tags.ndjson", cancellationToken);
        var collections = await ReadNdjsonAsync<LabelRecord>(archive, "metadata/collections.ndjson", cancellationToken);
        var tagMemberships = await ReadNdjsonAsync<MembershipRecord>(
            archive, "metadata/tag_memberships.ndjson", cancellationToken);
        var collectionMemberships = await ReadNdjsonAsync<MembershipRecord>(
            archive, "metadata/collection_memberships.ndjson", cancellationToken);
        return new PackageSnapshot(
            manifest, items, assets, tags, collections, tagMemberships, collectionMemberships);
    }

    private async Task<int> ImportBlobsAsync(
        string packagePath,
        IReadOnlyCollection<AssetRecord> assets,
        CancellationToken cancellationToken)
    {
        var imported = 0;
        await using var input = new FileStream(
            Path.GetFullPath(packagePath), FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var asset in assets.GroupBy(value => value.Sha256, StringComparer.Ordinal).Select(group => group.First()))
        {
            ValidateAsset(asset);
            if (await _blobStore.VerifyAsync(asset.Sha256, cancellationToken))
            {
                continue;
            }

            var entry = archive.GetEntry(asset.RelativePath) ??
                throw new InvalidDataException($"Falta el blob {asset.Sha256}.");
            var stagingRoot = Path.Combine(_profileRoot, "staging");
            Directory.CreateDirectory(stagingRoot);
            var temporary = Path.Combine(stagingRoot, $"sync-{Guid.NewGuid():N}.partial");
            try
            {
                await using (var source = entry.Open())
                await using (var output = new FileStream(
                    temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await source.CopyToAsync(output, cancellationToken);
                    await output.FlushAsync(cancellationToken);
                    output.Flush(flushToDisk: true);
                }

                var blob = await _blobStore.ImportFileAsync(temporary, cancellationToken);
                if (!string.Equals(blob.Sha256, asset.Sha256, StringComparison.Ordinal) ||
                    blob.ByteLength != asset.ByteLength)
                {
                    throw new InvalidDataException($"El blob {asset.Sha256} no coincide con su manifest.");
                }

                imported++;
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        return imported;
    }

    private async Task<Guid> ReadLocalDeviceIdAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = 'device_id';";
        return Guid.Parse((string)(await command.ExecuteScalarAsync(cancellationToken) ??
            throw new InvalidDataException("El perfil no tiene identidad de dispositivo.")));
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            ForeignKeys = true,
            DefaultTimeout = 5,
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<ItemRecord?> ReadItemAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM items WHERE id = $id;";
        command.Parameters.AddWithValue("$id", Guid.Parse(id).ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new ItemRecord(
            reader.GetString(reader.GetOrdinal("id")),
            reader.GetInt32(reader.GetOrdinal("kind")),
            reader.GetString(reader.GetOrdinal("title")),
            NullableString(reader, "original_url"),
            NullableString(reader, "canonical_url"),
            NullableString(reader, "source"),
            NullableString(reader, "author"),
            NullableString(reader, "description"),
            reader.GetString(reader.GetOrdinal("created_at")),
            reader.GetString(reader.GetOrdinal("updated_at")),
            NullableString(reader, "deleted_at"),
            reader.GetInt32(reader.GetOrdinal("favorite")) == 1) : null;
    }

    private static async Task InsertItemAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ItemRecord item,
        CancellationToken cancellationToken) =>
        await ExecuteItemAsync(connection, transaction, item, """
            INSERT INTO items(id, kind, title, original_url, canonical_url, source, author, description,
                created_at, updated_at, deleted_at, favorite)
            VALUES($id, $kind, $title, $original_url, $canonical_url, $source, $author, $description,
                $created_at, $updated_at, $deleted_at, $favorite);
            """, cancellationToken);

    private static async Task UpdateItemAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ItemRecord item,
        CancellationToken cancellationToken) =>
        await ExecuteItemAsync(connection, transaction, item, """
            UPDATE items SET kind=$kind, title=$title, original_url=$original_url,
                canonical_url=$canonical_url, source=$source, author=$author, description=$description,
                created_at=$created_at, updated_at=$updated_at, deleted_at=$deleted_at, favorite=$favorite
            WHERE id=$id;
            """, cancellationToken);

    private static async Task ExecuteItemAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ItemRecord item,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", item.Id);
        command.Parameters.AddWithValue("$kind", item.Kind);
        command.Parameters.AddWithValue("$title", item.Title);
        command.Parameters.AddWithValue("$original_url", (object?)item.OriginalUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$canonical_url", (object?)item.CanonicalUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$source", (object?)item.Source ?? DBNull.Value);
        command.Parameters.AddWithValue("$author", (object?)item.Author ?? DBNull.Value);
        command.Parameters.AddWithValue("$description", (object?)item.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", item.CreatedAt);
        command.Parameters.AddWithValue("$updated_at", item.UpdatedAt);
        command.Parameters.AddWithValue("$deleted_at", (object?)item.DeletedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$favorite", item.Favorite ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAssetIfMissingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AssetRecord asset,
        CancellationToken cancellationToken)
    {
        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT sha256, item_id FROM assets WHERE id = $id;";
        read.Parameters.AddWithValue("$id", asset.Id);
        await using (var reader = await read.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                if (!string.Equals(reader.GetString(0), asset.Sha256, StringComparison.Ordinal) ||
                    !string.Equals(reader.GetString(1), asset.ItemId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Colisión de identidad de asset: {asset.Id}.");
                }

                return;
            }
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO assets(id, item_id, sha256, byte_length, mime_type, original_name, relative_path, created_at)
            VALUES($id, $item_id, $sha256, $byte_length, $mime_type, $original_name, $relative_path, $created_at);
            """;
        insert.Parameters.AddWithValue("$id", Guid.Parse(asset.Id).ToString("D"));
        insert.Parameters.AddWithValue("$item_id", Guid.Parse(asset.ItemId).ToString("D"));
        insert.Parameters.AddWithValue("$sha256", asset.Sha256);
        insert.Parameters.AddWithValue("$byte_length", asset.ByteLength);
        insert.Parameters.AddWithValue("$mime_type", asset.MimeType);
        insert.Parameters.AddWithValue("$original_name", asset.OriginalName);
        insert.Parameters.AddWithValue("$relative_path", asset.RelativePath);
        insert.Parameters.AddWithValue("$created_at", asset.CreatedAt);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, string>> EnsureLabelsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        IReadOnlyCollection<LabelRecord> labels,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in labels)
        {
            var remoteId = Guid.Parse(label.Id).ToString("D");
            var normalized = LibraryTag.Normalize(label.Name);
            if (!string.Equals(normalized.NormalizedName, label.NormalizedName, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Etiqueta o colección remota sin normalización canónica.");
            }

            await using var find = connection.CreateCommand();
            find.Transaction = transaction;
            find.CommandText = $"SELECT id FROM {table} WHERE normalized_name=$normalized;";
            find.Parameters.AddWithValue("$normalized", label.NormalizedName);
            var existing = (string?)await find.ExecuteScalarAsync(cancellationToken);
            if (existing is null)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = $"INSERT INTO {table}(id,name,normalized_name) VALUES($id,$name,$normalized);";
                insert.Parameters.AddWithValue("$id", remoteId);
                insert.Parameters.AddWithValue("$name", normalized.Name);
                insert.Parameters.AddWithValue("$normalized", normalized.NormalizedName);
                await insert.ExecuteNonQueryAsync(cancellationToken);
                existing = remoteId;
            }

            result.Add(remoteId, existing);
        }

        return result;
    }

    private static async Task InsertMembershipIfMissingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string labelColumn,
        MembershipRecord membership,
        Dictionary<string, string> labelIds,
        CancellationToken cancellationToken)
    {
        var membershipId = Guid.Parse(membership.MembershipId).ToString("D");
        var itemId = Guid.Parse(membership.ItemId).ToString("D");
        var remoteLabelId = Guid.Parse(membership.LabelId).ToString("D");
        if (!labelIds.TryGetValue(remoteLabelId, out var localLabelId))
        {
            throw new InvalidDataException("Una membresía remota referencia una etiqueta inexistente.");
        }

        var removedAt = membership.RemovedAt is null
            ? null
            : ParseTimestamp(membership.RemovedAt).ToString("O", CultureInfo.InvariantCulture);
        var applyRemoteRemoval = false;
        await using var find = connection.CreateCommand();
        find.Transaction = transaction;
        find.CommandText = $"SELECT item_id,{labelColumn},removed_at FROM {table} WHERE membership_id=$id;";
        find.Parameters.AddWithValue("$id", membershipId);
        await using (var reader = await find.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                if (!string.Equals(reader.GetString(0), itemId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(reader.GetString(1), localLabelId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("Colisión de identidad de membresía.");
                }

                applyRemoteRemoval = reader.IsDBNull(2) && removedAt is not null;
                if (!applyRemoteRemoval)
                {
                    return;
                }
            }
        }

        if (applyRemoteRemoval)
        {
            await using var remove = connection.CreateCommand();
            remove.Transaction = transaction;
            remove.CommandText = $"UPDATE {table} SET removed_at=$removed WHERE membership_id=$id AND removed_at IS NULL;";
            remove.Parameters.AddWithValue("$id", membershipId);
            remove.Parameters.AddWithValue("$removed", removedAt!);
            await remove.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO {table}(membership_id,item_id,{labelColumn},removed_at) " +
            "VALUES($id,$item,$label,$removed);";
        insert.Parameters.AddWithValue("$id", membershipId);
        insert.Parameters.AddWithValue("$item", itemId);
        insert.Parameters.AddWithValue("$label", localLabelId);
        insert.Parameters.AddWithValue("$removed", (object?)removedAt ?? DBNull.Value);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<SyncState?> ReadStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid peerId,
        string itemId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT local_hash, remote_hash FROM sync_item_state
            WHERE peer_device_id = $peer AND item_id = $item;
            """;
        command.Parameters.AddWithValue("$peer", peerId.ToString("D"));
        command.Parameters.AddWithValue("$item", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new SyncState(reader.GetString(0), reader.GetString(1))
            : null;
    }

    private static async Task UpsertStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid peerId,
        string itemId,
        string localHash,
        string remoteHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sync_item_state(peer_device_id, item_id, local_hash, remote_hash, last_synced_at)
            VALUES($peer, $item, $local_hash, $remote_hash, $at)
            ON CONFLICT(peer_device_id, item_id) DO UPDATE SET
                local_hash=excluded.local_hash, remote_hash=excluded.remote_hash, last_synced_at=excluded.last_synced_at;
            """;
        command.Parameters.AddWithValue("$peer", peerId.ToString("D"));
        command.Parameters.AddWithValue("$item", itemId);
        command.Parameters.AddWithValue("$local_hash", localHash);
        command.Parameters.AddWithValue("$remote_hash", remoteHash);
        command.Parameters.AddWithValue("$at", now.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> HasUnresolvedConflictAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid peerId,
        string itemId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1 FROM conflicts
            WHERE entity_id=$item AND field_name=$field AND resolved_at IS NULL LIMIT 1;
            """;
        command.Parameters.AddWithValue("$item", itemId);
        command.Parameters.AddWithValue("$field", $"sync:{peerId:D}");
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task UpsertConflictAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid peerId,
        ItemRecord local,
        string localHash,
        ItemRecord remote,
        string remoteHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var field = $"sync:{peerId:D}";
        var payload = JsonSerializer.Serialize(
            new ConflictPayload(peerId, localHash, remoteHash, local, remote), JsonOptions);
        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE conflicts SET versions_json=$versions, created_at=$created_at
            WHERE entity_id=$item AND field_name=$field AND resolved_at IS NULL;
            """;
        update.Parameters.AddWithValue("$versions", payload);
        update.Parameters.AddWithValue("$created_at", now.ToString("O", CultureInfo.InvariantCulture));
        update.Parameters.AddWithValue("$item", remote.Id);
        update.Parameters.AddWithValue("$field", field);
        if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO conflicts(id, entity_id, field_name, versions_json, created_at, resolved_at)
                VALUES($id, $item, $field, $versions, $created_at, NULL);
                """;
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            insert.Parameters.AddWithValue("$item", remote.Id);
            insert.Parameters.AddWithValue("$field", field);
            insert.Parameters.AddWithValue("$versions", payload);
            insert.Parameters.AddWithValue("$created_at", now.ToString("O", CultureInfo.InvariantCulture));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task WriteOperationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ItemRecord item,
        string operationType,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var increment = connection.CreateCommand();
        increment.Transaction = transaction;
        increment.CommandText = "UPDATE settings SET value=CAST(value AS INTEGER)+1 WHERE key='next_counter';";
        await increment.ExecuteNonQueryAsync(cancellationToken);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO operations(operation_id, origin_device_id, origin_counter, entity_id, entity_type,
                operation_type, schema_version, payload, causal_context, created_at)
            SELECT $operation_id, d.value, CAST(c.value AS INTEGER), $entity_id, 'item',
                $operation_type, 1, $payload, '{}', $created_at
            FROM settings d, settings c WHERE d.key='device_id' AND c.key='next_counter';
            """;
        insert.Parameters.AddWithValue("$operation_id", Guid.NewGuid().ToString("D"));
        insert.Parameters.AddWithValue("$entity_id", item.Id);
        insert.Parameters.AddWithValue("$operation_type", operationType);
        insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(item, JsonOptions));
        insert.Parameters.AddWithValue("$created_at", now.ToString("O", CultureInfo.InvariantCulture));
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ItemRecord NormalizeAndValidate(ItemRecord record)
    {
        var id = Guid.Parse(record.Id).ToString("D");
        var kind = (ItemKind)record.Kind;
        if (!Enum.IsDefined(kind))
        {
            throw new InvalidDataException("Tipo de elemento remoto inválido.");
        }

        var originalUrl = string.IsNullOrWhiteSpace(record.OriginalUrl) ? null : record.OriginalUrl.Trim();
        var canonicalUrl = string.IsNullOrWhiteSpace(record.CanonicalUrl) ? null : record.CanonicalUrl.Trim();
        if (kind is ItemKind.Link or ItemKind.Hybrid)
        {
            var uri = UrlCanonicalizer.ParseHttpUrl(originalUrl ?? throw new InvalidDataException(
                "El elemento remoto necesita URL."));
            canonicalUrl ??= UrlCanonicalizer.Canonicalize(uri.AbsoluteUri);
        }
        else if (originalUrl is not null)
        {
            throw new InvalidDataException("Un archivo remoto no puede incluir URL.");
        }

        _ = LibraryItem.Rehydrate(
            Guid.Parse(id), kind, record.Title, originalUrl, canonicalUrl, record.Source, record.Author,
            record.Description, ParseTimestamp(record.CreatedAt), ParseTimestamp(record.UpdatedAt),
            record.DeletedAt is null ? null : ParseTimestamp(record.DeletedAt), record.Favorite);
        return record with
        {
            Id = id,
            Title = record.Title.Trim(),
            OriginalUrl = originalUrl,
            CanonicalUrl = canonicalUrl,
            Source = TrimNullable(record.Source),
            Author = TrimNullable(record.Author),
            Description = TrimNullable(record.Description),
            CreatedAt = ParseTimestamp(record.CreatedAt).ToString("O", CultureInfo.InvariantCulture),
            UpdatedAt = ParseTimestamp(record.UpdatedAt).ToString("O", CultureInfo.InvariantCulture),
            DeletedAt = record.DeletedAt is null
                ? null
                : ParseTimestamp(record.DeletedAt).ToString("O", CultureInfo.InvariantCulture),
        };
    }

    private static void ValidateAsset(AssetRecord asset)
    {
        _ = Guid.Parse(asset.Id);
        _ = Guid.Parse(asset.ItemId);
        if (asset.Sha256.Length != 64 || !asset.Sha256.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new InvalidDataException("SHA-256 de asset inválido.");
        }

        var expected = $"blobs/{asset.Sha256[..2]}/{asset.Sha256.Substring(2, 2)}/{asset.Sha256}";
        if (!string.Equals(asset.RelativePath, expected, StringComparison.Ordinal) || asset.ByteLength < 0)
        {
            throw new InvalidDataException("Manifest de asset inválido.");
        }
    }

    private static void ValidatePackageRelations(
        IReadOnlyCollection<ItemRecord> items,
        IReadOnlyCollection<AssetRecord> assets)
    {
        var itemIds = items.Select(item => Guid.Parse(item.Id).ToString("D"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var itemIdsWithAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            var itemId = Guid.Parse(asset.ItemId).ToString("D");
            if (!itemIds.Contains(itemId))
            {
                throw new InvalidDataException("Un asset remoto referencia un elemento inexistente.");
            }

            itemIdsWithAssets.Add(itemId);
        }

        foreach (var item in items.Where(item => item.Kind is (int)ItemKind.File or (int)ItemKind.Hybrid))
        {
            if (!itemIdsWithAssets.Contains(Guid.Parse(item.Id).ToString("D")))
            {
                throw new InvalidDataException("Un archivo o híbrido remoto no contiene ningún asset.");
            }
        }
    }

    private static string HashItem(ItemRecord item) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(item, JsonOptions))).ToLowerInvariant();

    private static string? NullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string? TrimNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    private sealed record ConflictPayload(
        Guid SourceDeviceId,
        string LocalHash,
        string RemoteHash,
        ItemRecord Local,
        ItemRecord Remote);

    private sealed record SyncState(string LocalHash, string RemoteHash);

    private sealed record PackageSnapshot(
        BackupManifest Manifest,
        List<ItemRecord> Items,
        List<AssetRecord> Assets,
        List<LabelRecord> Tags,
        List<LabelRecord> Collections,
        List<MembershipRecord> TagMemberships,
        List<MembershipRecord> CollectionMemberships);
}
