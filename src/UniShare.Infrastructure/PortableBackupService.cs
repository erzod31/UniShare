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

public sealed class PortableBackupService : IBackupService, ISelectiveBackupService, IDeltaBackupService
{
    private const int MaximumEntries = 1_000_000;
    private const long MaximumUncompressedBytes = 100L * 1024 * 1024 * 1024;

    private readonly string _profileRoot;
    private readonly string _databasePath;

    public PortableBackupService(string profileRoot)
    {
        _profileRoot = Path.GetFullPath(profileRoot);
        _databasePath = Path.Combine(_profileRoot, "library.db");
    }

    public Task<BackupSummary> ExportAsync(
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        ExportCoreAsync(destinationPath, null, null, null, cancellationToken);

    public Task<BackupSummary> ExportAsync(
        string destinationPath,
        IReadOnlySet<string> includedBlobHashes,
        IReadOnlySet<string> availableBlobHashes,
        CancellationToken cancellationToken = default) =>
        ExportCoreAsync(
            destinationPath,
            null,
            includedBlobHashes ?? throw new ArgumentNullException(nameof(includedBlobHashes)),
            availableBlobHashes ?? throw new ArgumentNullException(nameof(availableBlobHashes)),
            cancellationToken);

    public Task<BackupSummary> ExportDeltaAsync(
        string destinationPath,
        IReadOnlySet<string> includedItemIds,
        IReadOnlySet<string> includedBlobHashes,
        IReadOnlySet<string> availableBlobHashes,
        CancellationToken cancellationToken = default) =>
        ExportCoreAsync(
            destinationPath,
            includedItemIds ?? throw new ArgumentNullException(nameof(includedItemIds)),
            includedBlobHashes ?? throw new ArgumentNullException(nameof(includedBlobHashes)),
            availableBlobHashes ?? throw new ArgumentNullException(nameof(availableBlobHashes)),
            cancellationToken);

    private async Task<BackupSummary> ExportCoreAsync(
        string destinationPath,
        IReadOnlySet<string>? includedItemIds,
        IReadOnlySet<string>? includedBlobHashes,
        IReadOnlySet<string>? availableBlobHashes,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_databasePath))
        {
            throw new FileNotFoundException("No existe la base de datos del perfil.", _databasePath);
        }

        var destination = Path.GetFullPath(destinationPath);
        if (File.Exists(destination))
        {
            throw new IOException("El destino ya existe; UniShare no sobrescribe respaldos silenciosamente.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidOperationException(
            "El respaldo necesita un directorio padre."));
        var temporaryPath = destination + $".partial-{Guid.NewGuid():N}";
        try
        {
            var snapshot = FilterSnapshot(await ReadSnapshotAsync(cancellationToken), includedItemIds);
            var createdAtUtc = DateTimeOffset.UtcNow;
            var checksums = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var uniqueBlobs = snapshot.Assets
                .GroupBy(asset => asset.Sha256, StringComparer.Ordinal)
                .Select(group => group.First())
                .Where(asset => includedBlobHashes is null || includedBlobHashes.Contains(asset.Sha256))
                .OrderBy(asset => asset.Sha256, StringComparer.Ordinal)
                .ToArray();
            long totalBlobBytes = 0;

            await using (var output = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
            {
                checksums.Add("metadata/items.ndjson", await WriteNdjsonAsync(
                    archive, "metadata/items.ndjson", snapshot.Items, cancellationToken));
                checksums.Add("metadata/assets.ndjson", await WriteNdjsonAsync(
                    archive, "metadata/assets.ndjson", snapshot.Assets, cancellationToken));
                checksums.Add("metadata/tags.ndjson", await WriteNdjsonAsync(
                    archive, "metadata/tags.ndjson", snapshot.Tags, cancellationToken));
                checksums.Add("metadata/collections.ndjson", await WriteNdjsonAsync(
                    archive, "metadata/collections.ndjson", snapshot.Collections, cancellationToken));
                checksums.Add("metadata/tag_memberships.ndjson", await WriteNdjsonAsync(
                    archive, "metadata/tag_memberships.ndjson", snapshot.TagMemberships, cancellationToken));
                checksums.Add("metadata/collection_memberships.ndjson", await WriteNdjsonAsync(
                    archive, "metadata/collection_memberships.ndjson", snapshot.CollectionMemberships, cancellationToken));
                if (availableBlobHashes is not null)
                {
                    checksums.Add("sync/available_blobs.ndjson", await WriteNdjsonAsync(
                        archive,
                        "sync/available_blobs.ndjson",
                        availableBlobHashes.Order(StringComparer.Ordinal).Select(hash => new BlobAvailabilityRecord(hash)),
                        cancellationToken));
                }

                foreach (var blob in uniqueBlobs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sourcePath = ResolveProfilePath(blob.RelativePath);
                    if (!File.Exists(sourcePath))
                    {
                        throw new FileNotFoundException($"Falta el blob {blob.Sha256}.", sourcePath);
                    }

                    var entryPath = BlobEntryPath(blob.Sha256);
                    var (checksum, bytes) = await WriteFileEntryAsync(
                        archive, entryPath, sourcePath, cancellationToken);
                    if (!string.Equals(checksum, blob.Sha256, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"El blob {blob.Sha256} está dañado.");
                    }

                    checksums.Add(entryPath, checksum);
                    totalBlobBytes += bytes;
                }

                var manifest = new BackupManifest(
                    FormatVersion,
                    createdAtUtc,
                    snapshot.Items.Count,
                    snapshot.Assets.Count,
                    snapshot.Tags.Count,
                    snapshot.Collections.Count,
                    uniqueBlobs.Length,
                    totalBlobBytes,
                    snapshot.DeviceId);
                checksums.Add("manifest.json", await WriteJsonAsync(
                    archive, "manifest.json", manifest, cancellationToken));
                _ = await WriteTextAsync(
                    archive,
                    "checksums.sha256",
                    string.Concat(checksums.Select(pair => $"{pair.Value}  {pair.Key}\n")),
                    cancellationToken);
            }

            File.Move(temporaryPath, destination);
            return new BackupSummary(
                FormatVersion,
                createdAtUtc,
                snapshot.Items.Count,
                snapshot.Assets.Count,
                snapshot.Tags.Count,
                snapshot.Collections.Count,
                uniqueBlobs.Length,
                totalBlobBytes);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static Snapshot FilterSnapshot(Snapshot snapshot, IReadOnlySet<string>? includedItemIds)
    {
        if (includedItemIds is null)
        {
            return snapshot;
        }

        var normalizedIds = includedItemIds
            .Select(id => Guid.Parse(id).ToString("D"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = snapshot.Items.Where(item => normalizedIds.Contains(item.Id)).ToList();
        var assets = snapshot.Assets.Where(asset => normalizedIds.Contains(asset.ItemId)).ToList();
        var tagMemberships = snapshot.TagMemberships
            .Where(membership => normalizedIds.Contains(membership.ItemId)).ToList();
        var collectionMemberships = snapshot.CollectionMemberships
            .Where(membership => normalizedIds.Contains(membership.ItemId)).ToList();
        var tagIds = tagMemberships.Select(membership => membership.LabelId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var collectionIds = collectionMemberships.Select(membership => membership.LabelId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new Snapshot(
            items,
            assets,
            snapshot.Tags.Where(tag => tagIds.Contains(tag.Id)).ToList(),
            snapshot.Collections.Where(collection => collectionIds.Contains(collection.Id)).ToList(),
            tagMemberships,
            collectionMemberships,
            snapshot.DeviceId);
    }

    public async Task<BackupSummary> InspectAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        await using var input = new FileStream(
            Path.GetFullPath(backupPath), FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
        var entries = ValidateEntrySet(archive);
        var checksums = await ReadChecksumsAsync(GetRequiredEntry(entries, "checksums.sha256"), cancellationToken);

        foreach (var entry in entries.Values.Where(entry => entry.FullName != "checksums.sha256"))
        {
            if (!checksums.TryGetValue(entry.FullName, out var expected))
            {
                throw new InvalidDataException($"Falta checksum para {entry.FullName}.");
            }

            var actual = await HashEntryAsync(entry, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual), Convert.FromHexString(expected)))
            {
                throw new InvalidDataException($"Checksum inválido en {entry.FullName}.");
            }
        }

        if (checksums.Keys.Any(path => !entries.ContainsKey(path)))
        {
            throw new InvalidDataException("El fichero de checksums referencia entradas ausentes.");
        }

        var manifest = await ReadJsonAsync<BackupManifest>(
            GetRequiredEntry(entries, "manifest.json"), cancellationToken);
        if (manifest.FormatVersion != FormatVersion)
        {
            throw new NotSupportedException($"Versión de respaldo no compatible: {manifest.FormatVersion}.");
        }

        var itemCount = await CountNdjsonAsync(GetRequiredEntry(entries, "metadata/items.ndjson"), cancellationToken);
        var assetCount = await CountNdjsonAsync(GetRequiredEntry(entries, "metadata/assets.ndjson"), cancellationToken);
        var tagCount = await CountNdjsonAsync(GetRequiredEntry(entries, "metadata/tags.ndjson"), cancellationToken);
        var collectionCount = await CountNdjsonAsync(GetRequiredEntry(entries, "metadata/collections.ndjson"), cancellationToken);
        var blobEntries = entries.Values.Where(entry => entry.FullName.StartsWith("blobs/", StringComparison.Ordinal)).ToArray();
        if (itemCount != manifest.ItemCount || assetCount != manifest.AssetCount ||
            tagCount != manifest.TagCount || collectionCount != manifest.CollectionCount ||
            blobEntries.Length != manifest.BlobCount || blobEntries.Sum(entry => entry.Length) != manifest.TotalBlobBytes)
        {
            throw new InvalidDataException("Los recuentos del manifest no coinciden con el contenido.");
        }

        foreach (var blob in blobEntries)
        {
            var hash = Path.GetFileName(blob.FullName);
            ValidateHash(hash);
            if (!string.Equals(checksums[blob.FullName], hash, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"El nombre del blob no coincide con su hash: {blob.FullName}.");
            }
        }

        return new BackupSummary(
            manifest.FormatVersion,
            manifest.CreatedAtUtc,
            manifest.ItemCount,
            manifest.AssetCount,
            manifest.TagCount,
            manifest.CollectionCount,
            manifest.BlobCount,
            manifest.TotalBlobBytes);
    }

    public async Task<BackupSummary> RestoreToNewProfileAsync(
        string backupPath,
        string destinationProfile,
        CancellationToken cancellationToken = default)
    {
        var summary = await InspectAsync(backupPath, cancellationToken);
        var destination = Path.GetFullPath(destinationProfile);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException("La restauración requiere una ruta de perfil que todavía no exista.");
        }

        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException(
            "El perfil de destino necesita un directorio padre.");
        Directory.CreateDirectory(parent);
        var temporaryProfile = destination + $".restore-{Guid.NewGuid():N}";
        Directory.CreateDirectory(temporaryProfile);

        try
        {
            await using var input = new FileStream(
                Path.GetFullPath(backupPath), FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
            var entries = ValidateEntrySet(archive);
            var items = await ReadNdjsonAsync<ItemRecord>(
                GetRequiredEntry(entries, "metadata/items.ndjson"), cancellationToken);
            var assets = await ReadNdjsonAsync<AssetRecord>(
                GetRequiredEntry(entries, "metadata/assets.ndjson"), cancellationToken);
            var tags = await ReadNdjsonAsync<LabelRecord>(
                GetRequiredEntry(entries, "metadata/tags.ndjson"), cancellationToken);
            var collections = await ReadNdjsonAsync<LabelRecord>(
                GetRequiredEntry(entries, "metadata/collections.ndjson"), cancellationToken);
            var tagMemberships = await ReadNdjsonAsync<MembershipRecord>(
                GetRequiredEntry(entries, "metadata/tag_memberships.ndjson"), cancellationToken);
            var collectionMemberships = await ReadNdjsonAsync<MembershipRecord>(
                GetRequiredEntry(entries, "metadata/collection_memberships.ndjson"), cancellationToken);

            foreach (var blobEntry in entries.Values.Where(entry => entry.FullName.StartsWith("blobs/", StringComparison.Ordinal)))
            {
                var hash = Path.GetFileName(blobEntry.FullName);
                var relativePath = BlobEntryPath(hash);
                var destinationPath = ResolveUnderRoot(temporaryProfile, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await using var source = blobEntry.Open();
                await using var output = new FileStream(
                    destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await source.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            var databasePath = Path.Combine(temporaryProfile, "library.db");
            var repository = new SqliteLibraryRepository(databasePath);
            await repository.InitializeAsync(cancellationToken);
            await ImportSnapshotAsync(
                databasePath, items, assets, tags, collections, tagMemberships, collectionMemberships, cancellationToken);
            SqliteConnection.ClearAllPools();
            Directory.Move(temporaryProfile, destination);
            return summary;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(temporaryProfile))
            {
                Directory.Delete(temporaryProfile, recursive: true);
            }
        }
    }

    private async Task<Snapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly;Foreign Keys=True");
        await connection.OpenAsync(cancellationToken);
        var items = await ReadRowsAsync(connection, "SELECT * FROM items ORDER BY id;", reader => new ItemRecord(
            reader.GetString(reader.GetOrdinal("id")),
            reader.GetInt32(reader.GetOrdinal("kind")),
            reader.GetString(reader.GetOrdinal("title")),
            ReadNullableString(reader, "original_url"),
            ReadNullableString(reader, "canonical_url"),
            ReadNullableString(reader, "source"),
            ReadNullableString(reader, "author"),
            ReadNullableString(reader, "description"),
            reader.GetString(reader.GetOrdinal("created_at")),
            reader.GetString(reader.GetOrdinal("updated_at")),
            ReadNullableString(reader, "deleted_at"),
            reader.GetInt32(reader.GetOrdinal("favorite")) == 1), cancellationToken);
        var assets = await ReadRowsAsync(connection, "SELECT * FROM assets ORDER BY id;", reader => new AssetRecord(
            reader.GetString(reader.GetOrdinal("id")),
            reader.GetString(reader.GetOrdinal("item_id")),
            reader.GetString(reader.GetOrdinal("sha256")),
            reader.GetInt64(reader.GetOrdinal("byte_length")),
            reader.GetString(reader.GetOrdinal("mime_type")),
            reader.GetString(reader.GetOrdinal("original_name")),
            reader.GetString(reader.GetOrdinal("relative_path")),
            reader.GetString(reader.GetOrdinal("created_at"))), cancellationToken);
        var tags = await ReadRowsAsync(connection, "SELECT * FROM tags ORDER BY id;", reader => new LabelRecord(
            reader.GetString(0), reader.GetString(1), reader.GetString(2)), cancellationToken);
        var collections = await ReadRowsAsync(connection, "SELECT * FROM collections ORDER BY id;", reader => new LabelRecord(
            reader.GetString(0), reader.GetString(1), reader.GetString(2)), cancellationToken);
        var tagMemberships = await ReadRowsAsync(connection, "SELECT * FROM item_tags ORDER BY membership_id;", reader => new MembershipRecord(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), ReadNullableString(reader, "removed_at")), cancellationToken);
        var collectionMemberships = await ReadRowsAsync(connection, "SELECT * FROM item_collections ORDER BY membership_id;", reader => new MembershipRecord(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), ReadNullableString(reader, "removed_at")), cancellationToken);
        await using var deviceCommand = connection.CreateCommand();
        deviceCommand.CommandText = "SELECT value FROM settings WHERE key = 'device_id';";
        var deviceId = Guid.Parse((string)(await deviceCommand.ExecuteScalarAsync(cancellationToken) ??
            throw new InvalidDataException("El perfil no tiene identidad de dispositivo.")));
        return new Snapshot(items, assets, tags, collections, tagMemberships, collectionMemberships, deviceId);
    }

    private static async Task ImportSnapshotAsync(
        string databasePath,
        IReadOnlyCollection<ItemRecord> items,
        IReadOnlyCollection<AssetRecord> assets,
        IReadOnlyCollection<LabelRecord> tags,
        IReadOnlyCollection<LabelRecord> collections,
        IReadOnlyCollection<MembershipRecord> tagMemberships,
        IReadOnlyCollection<MembershipRecord> collectionMemberships,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Foreign Keys=True");
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        foreach (var record in items)
        {
            var item = LibraryItem.Rehydrate(
                Guid.Parse(record.Id), (ItemKind)record.Kind, record.Title, record.OriginalUrl, record.CanonicalUrl,
                record.Source, record.Author, record.Description, ParseTimestamp(record.CreatedAt),
                ParseTimestamp(record.UpdatedAt), record.DeletedAt is null ? null : ParseTimestamp(record.DeletedAt), record.Favorite);
            if (item.Kind is ItemKind.Link or ItemKind.Hybrid)
            {
                _ = UrlCanonicalizer.ParseHttpUrl(item.OriginalUrl!);
            }

            await ExecuteAsync(connection, transaction, """
                INSERT INTO items(id, kind, title, original_url, canonical_url, source, author, description,
                    created_at, updated_at, deleted_at, favorite)
                VALUES($id, $kind, $title, $original_url, $canonical_url, $source, $author, $description,
                    $created_at, $updated_at, $deleted_at, $favorite);
                """, new Dictionary<string, object?>
            {
                ["$id"] = item.Id.ToString("D"),
                ["$kind"] = (int)item.Kind,
                ["$title"] = item.Title,
                ["$original_url"] = item.OriginalUrl,
                ["$canonical_url"] = item.CanonicalUrl,
                ["$source"] = item.Source,
                ["$author"] = item.Author,
                ["$description"] = item.Description,
                ["$created_at"] = record.CreatedAt,
                ["$updated_at"] = record.UpdatedAt,
                ["$deleted_at"] = record.DeletedAt,
                ["$favorite"] = item.Favorite ? 1 : 0,
            }, cancellationToken);
        }

        foreach (var record in assets)
        {
            var asset = new AssetManifest(
                Guid.Parse(record.Id), Guid.Parse(record.ItemId), record.Sha256, record.ByteLength,
                record.MimeType, record.OriginalName, record.RelativePath, ParseTimestamp(record.CreatedAt));
            if (!string.Equals(asset.RelativePath, BlobEntryPath(asset.Sha256), StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Ruta de asset no canónica: {asset.RelativePath}.");
            }

            await ExecuteAsync(connection, transaction, """
                INSERT INTO assets(id, item_id, sha256, byte_length, mime_type, original_name, relative_path, created_at)
                VALUES($id, $item_id, $sha256, $byte_length, $mime_type, $original_name, $relative_path, $created_at);
                """, new Dictionary<string, object?>
            {
                ["$id"] = asset.Id.ToString("D"),
                ["$item_id"] = asset.ItemId.ToString("D"),
                ["$sha256"] = asset.Sha256,
                ["$byte_length"] = asset.ByteLength,
                ["$mime_type"] = asset.MimeType,
                ["$original_name"] = asset.OriginalName,
                ["$relative_path"] = asset.RelativePath,
                ["$created_at"] = record.CreatedAt,
            }, cancellationToken);
        }

        await ImportLabelsAsync(connection, transaction, "tags", tags, cancellationToken);
        await ImportLabelsAsync(connection, transaction, "collections", collections, cancellationToken);
        await ImportMembershipsAsync(connection, transaction, "item_tags", "tag_id", tagMemberships, cancellationToken);
        await ImportMembershipsAsync(connection, transaction, "item_collections", "collection_id", collectionMemberships, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await using var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals((string?)await integrity.ExecuteScalarAsync(cancellationToken), "ok", StringComparison.Ordinal))
        {
            throw new InvalidDataException("La base restaurada no supera integrity_check.");
        }
    }

    private static async Task ImportLabelsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        IEnumerable<LabelRecord> records,
        CancellationToken cancellationToken)
    {
        foreach (var record in records)
        {
            var (_, normalized) = LibraryTag.Normalize(record.Name);
            if (!string.Equals(normalized, record.NormalizedName, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Nombre normalizado inválido: {record.Name}.");
            }

            await ExecuteAsync(connection, transaction,
                $"INSERT INTO {table}(id, name, normalized_name) VALUES($id, $name, $normalized_name);",
                new Dictionary<string, object?>
                {
                    ["$id"] = Guid.Parse(record.Id).ToString("D"),
                    ["$name"] = record.Name,
                    ["$normalized_name"] = record.NormalizedName,
                }, cancellationToken);
        }
    }

    private static async Task ImportMembershipsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string foreignKey,
        IEnumerable<MembershipRecord> records,
        CancellationToken cancellationToken)
    {
        foreach (var record in records)
        {
            await ExecuteAsync(connection, transaction,
                $"INSERT INTO {table}(membership_id, item_id, {foreignKey}, removed_at) VALUES($membership_id, $item_id, $label_id, $removed_at);",
                new Dictionary<string, object?>
                {
                    ["$membership_id"] = Guid.Parse(record.MembershipId).ToString("D"),
                    ["$item_id"] = Guid.Parse(record.ItemId).ToString("D"),
                    ["$label_id"] = Guid.Parse(record.LabelId).ToString("D"),
                    ["$removed_at"] = record.RemovedAt,
                }, cancellationToken);
        }
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<T>> ReadRowsAsync<T>(
        SqliteConnection connection,
        string sql,
        Func<SqliteDataReader, T> mapper,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var values = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(mapper(reader));
        }

        return values;
    }

    private static Dictionary<string, ZipArchiveEntry> ValidateEntrySet(ZipArchive archive)
    {
        if (archive.Entries.Count is 0 or > MaximumEntries)
        {
            throw new InvalidDataException("Número de entradas de respaldo no permitido.");
        }

        if (archive.Entries.Sum(entry => entry.Length) > MaximumUncompressedBytes)
        {
            throw new InvalidDataException("El respaldo supera el tamaño permitido.");
        }

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            ValidateEntryPath(entry.FullName);
            if (!entries.TryAdd(entry.FullName, entry))
            {
                throw new InvalidDataException($"Entrada duplicada: {entry.FullName}.");
            }
        }

        return entries;
    }

    private static void ValidateEntryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') ||
            path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new InvalidDataException($"Ruta no permitida en el respaldo: {path}.");
        }
    }

    private static ZipArchiveEntry GetRequiredEntry(
        Dictionary<string, ZipArchiveEntry> entries,
        string path) =>
        entries.TryGetValue(path, out var entry)
            ? entry
            : throw new InvalidDataException($"Falta la entrada {path}.");

    private static async Task<string> WriteNdjsonAsync<T>(
        ZipArchive archive,
        string path,
        IEnumerable<T> records,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var output = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var record in records)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);
            await output.WriteAsync(bytes, cancellationToken);
            await output.WriteAsync("\n"u8.ToArray(), cancellationToken);
            hash.AppendData(bytes);
            hash.AppendData("\n"u8);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static Task<string> WriteJsonAsync<T>(
        ZipArchive archive,
        string path,
        T value,
        CancellationToken cancellationToken) =>
        WriteTextAsync(archive, path, JsonSerializer.Serialize(value, JsonOptions) + "\n", cancellationToken);

    private static async Task<string> WriteTextAsync(
        ZipArchive archive,
        string path,
        string text,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var output = entry.Open();
        await output.WriteAsync(bytes, cancellationToken);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static async Task<(string Hash, long Bytes)> WriteFileEntryAsync(
        ZipArchive archive,
        string path,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        await using var input = new FileStream(
            sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long bytes = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hash.AppendData(buffer, 0, read);
            bytes += read;
        }

        return (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), bytes);
    }

    private static async Task<Dictionary<string, string>> ReadChecksumsAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        var lines = await ReadLinesAsync(entry, cancellationToken);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var separator = line.IndexOf("  ", StringComparison.Ordinal);
            if (separator != 64)
            {
                throw new InvalidDataException("Línea de checksum inválida.");
            }

            var hash = line[..separator];
            ValidateHash(hash);
            var path = line[(separator + 2)..];
            ValidateEntryPath(path);
            if (!values.TryAdd(path, hash))
            {
                throw new InvalidDataException($"Checksum duplicado para {path}.");
            }
        }

        return values;
    }

    private static async Task<string> HashEntryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static async Task<int> CountNdjsonAsync(ZipArchiveEntry entry, CancellationToken cancellationToken) =>
        (await ReadLinesAsync(entry, cancellationToken)).Count;

    private string ResolveProfilePath(string relativePath) => ResolveUnderRoot(_profileRoot, relativePath);

    private static string ResolveUnderRoot(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("La ruta debe ser relativa.");
        }

        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("La ruta sale del perfil.");
        }

        return fullPath;
    }

    private static string BlobEntryPath(string hash)
    {
        ValidateHash(hash);
        return $"blobs/{hash[..2]}/{hash.Substring(2, 2)}/{hash}";
    }

    private static void ValidateHash(string hash)
    {
        if (hash.Length != 64 || !hash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new InvalidDataException("SHA-256 inválido.");
        }
    }

    private static string? ReadNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record Snapshot(
        List<ItemRecord> Items,
        List<AssetRecord> Assets,
        List<LabelRecord> Tags,
        List<LabelRecord> Collections,
        List<MembershipRecord> TagMemberships,
        List<MembershipRecord> CollectionMemberships,
        Guid DeviceId);

    private sealed record BlobAvailabilityRecord(string Sha256);
}
