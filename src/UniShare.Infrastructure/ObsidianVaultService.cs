using System.Security.Cryptography;
using System.Text;
using UniShare.Application;

namespace UniShare.Infrastructure;

public sealed class ObsidianVaultService(
    IItemRepository repository,
    IBlobStore blobStore,
    TimeProvider timeProvider) : IObsidianVaultService
{
    private const string ManagedStart = "<!-- unishare:managed:start -->";
    private const string ManagedEnd = "<!-- unishare:managed:end -->";
    private const string NotesStart = "<!-- unishare:notes:start -->";
    private const string NotesEnd = "<!-- unishare:notes:end -->";
    private const string IndexFileName = "Índice.md";

    public async Task<ObsidianExportResult> ExportAsync(
        string vaultRoot,
        CancellationToken cancellationToken = default)
    {
        var root = ValidateVaultRoot(vaultRoot);
        var notesRoot = Path.Combine(root, "UniShare");
        var assetsRoot = Path.Combine(notesRoot, "assets");
        Directory.CreateDirectory(notesRoot);
        Directory.CreateDirectory(assetsRoot);
        var indexPath = Path.Combine(notesRoot, IndexFileName);
        if (File.Exists(indexPath) && !HasFrontMatterValue(
            await File.ReadAllTextAsync(indexPath, cancellationToken), "unishare_index", "true"))
        {
            throw new InvalidDataException("El índice de destino de Obsidian no pertenece a UniShare. Conserva el archivo o elige otra bóveda.");
        }

        var items = await repository.GetAllAsync(includeDeleted: false, cancellationToken);
        var itemIds = items.Select(item => item.Id).ToArray();
        var organizationsByItem = await repository.GetOrganizationsBatchAsync(itemIds, cancellationToken);
        var assetsByItem = await repository.GetAssetsBatchAsync(itemIds, cancellationToken);
        var assetCount = 0;
        var preservedNotes = 0;
        var expectedNotes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var expectedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var previouslyReferencedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var notePath = Path.Combine(notesRoot, $"{item.Id:D}.md");
            expectedNotes.Add(notePath);
            var existingText = File.Exists(notePath)
                ? await File.ReadAllTextAsync(notePath, cancellationToken)
                : null;
            var existingNotes = existingText is null ? null : ExtractNotes(existingText);
            if (existingText is not null && !IsManagedNote(existingText, item.Id))
            {
                throw new InvalidDataException("La nota de destino de Obsidian no tiene marcadores válidos de UniShare. Conserva el archivo o elige otra bóveda.");
            }
            if (existingNotes is not null)
            {
                preservedNotes++;
            }
            if (existingText is not null && IsManagedNote(existingText, item.Id))
            {
                CollectManagedAssetPaths(existingText, assetsRoot, previouslyReferencedAssets);
            }

            var organization = organizationsByItem.TryGetValue(item.Id, out var foundOrg)
                ? foundOrg
                : new ItemOrganization([], []);
            var assets = assetsByItem.TryGetValue(item.Id, out var foundAssets)
                ? foundAssets
                : [];
            foreach (var asset in assets)
            {
                var extension = SafeExtension(asset.OriginalName);
                var destinationName = $"{asset.Id:D}{extension}";
                var destination = Path.Combine(assetsRoot, destinationName);
                expectedAssets.Add(destination);
                var source = blobStore.GetAbsolutePath(asset.RelativePath);
                if (!await MatchesAssetAsync(destination, asset, cancellationToken))
                {
                    if (!await MatchesAssetAsync(source, asset, cancellationToken))
                    {
                        throw new InvalidDataException(
                            $"El archivo original de {asset.OriginalName} está ausente o dañado.");
                    }
                    await CopyAtomicallyAsync(source, destination, cancellationToken);
                }

                assetCount++;
            }

            var text = RenderNote(
                item,
                organization,
                assets,
                existingNotes ?? item.Description ?? string.Empty);
            if (existingText is not null)
            {
                text = ReplaceManagedBlock(existingText, text);
            }
            await WriteAtomicallyAsync(notePath, text, cancellationToken);
        }

        preservedNotes += await RemoveStaleManagedNotesAsync(
            notesRoot, assetsRoot, expectedNotes, previouslyReferencedAssets, cancellationToken);
        var externallyReferencedAssets = await FindReferencedAssetNamesAsync(
            vaultRoot,
            previouslyReferencedAssets
                .Where(path => !expectedAssets.Contains(path))
                .Select(Path.GetFileName)
                .OfType<string>(),
            cancellationToken);
        foreach (var staleAsset in previouslyReferencedAssets.Where(path => !expectedAssets.Contains(path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(staleAsset) &&
                !externallyReferencedAssets.Contains(Path.GetFileName(staleAsset)))
            {
                File.Delete(staleAsset);
            }
        }

        await WriteAtomicallyAsync(
            indexPath,
            RenderIndex(items, timeProvider.GetUtcNow()),
            cancellationToken);

        return new ObsidianExportResult(items.Count, assetCount, preservedNotes);
    }

    public async Task<ObsidianImportResult> ImportNotesAsync(
        string vaultRoot,
        CancellationToken cancellationToken = default)
    {
        var notesRoot = Path.Combine(ValidateVaultRoot(vaultRoot), "UniShare");
        if (!Directory.Exists(notesRoot))
        {
            throw new DirectoryNotFoundException(
                "La carpeta elegida no contiene la exportación UniShare esperada.");
        }

        var updated = 0;
        var unchanged = 0;
        var ignored = 0;
        foreach (var notePath in Directory.EnumerateFiles(notesRoot, "*.md", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetFileName(notePath), IndexFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (!Guid.TryParse(Path.GetFileNameWithoutExtension(notePath), out var id))
            {
                ignored++;
                continue;
            }

            var item = await repository.GetAsync(id, cancellationToken);
            var text = await File.ReadAllTextAsync(notePath, cancellationToken);
            var notes = ExtractNotes(text);
            if (item is null || item.IsDeleted || notes is null || !IsManagedNote(text, id))
            {
                ignored++;
                continue;
            }

            var normalizedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            if (string.Equals(item.Description, normalizedNotes, StringComparison.Ordinal))
            {
                unchanged++;
                continue;
            }

            await repository.UpdateAsync(
                item.UpdateMetadata(
                    item.Title,
                    item.Source,
                    item.Author,
                    normalizedNotes,
                    item.Favorite,
                    timeProvider.GetUtcNow()),
                cancellationToken);
            updated++;
        }

        return new ObsidianImportResult(updated, unchanged, ignored);
    }

    private static string RenderIndex(
        IReadOnlyCollection<Domain.LibraryItem> items,
        DateTimeOffset generatedAt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine("unishare_index: true");
        builder.Append("updated: ").AppendLine(generatedAt.ToString("O"));
        builder.AppendLine("---");
        builder.AppendLine("# Biblioteca UniShare");
        builder.AppendLine();
        builder.Append("Elementos: **").Append(items.Count).AppendLine("**");
        builder.AppendLine();
        foreach (var item in items.OrderBy(value => value.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            builder.Append("- [[")
                .Append(item.Id.ToString("D"))
                .Append('|')
                .Append(WikiAlias(item.Title))
                .Append("]] · ")
                .AppendLine(item.Kind.ToString());
        }
        builder.AppendLine();
        builder.AppendLine("> Este índice se regenera desde UniShare. Edita las notas dentro de cada elemento.");
        return builder.ToString();
    }

    private static string RenderNote(
        Domain.LibraryItem item,
        ItemOrganization organization,
        IReadOnlyList<Domain.AssetManifest> assets,
        string notes)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.Append("unishare_id: ").AppendLine(item.Id.ToString("D"));
        builder.AppendLine("---");
        builder.AppendLine(ManagedStart);
        builder.Append("# ").AppendLine(SingleLine(item.Title));
        builder.AppendLine();
        builder.Append("- Tipo: ").AppendLine(item.Kind.ToString());
        builder.Append("- Creado: ").AppendLine(item.CreatedAt.ToString("O"));
        builder.Append("- Actualizado: ").AppendLine(item.UpdatedAt.ToString("O"));
        if (item.OriginalUrl is not null)
        {
            builder.Append("- Enlace: [abrir](").Append(item.OriginalUrl).AppendLine(")");
        }
        if (item.Source is not null)
        {
            builder.Append("- Fuente: ").AppendLine(SingleLine(item.Source));
        }
        if (item.Author is not null)
        {
            builder.Append("- Autor: ").AppendLine(SingleLine(item.Author));
        }
        if (organization.Tags.Count > 0)
        {
            builder.Append("- Etiquetas: ").AppendLine(string.Join(", ", organization.Tags.Select(tag => SingleLine(tag.Name))));
        }
        if (organization.Collections.Count > 0)
        {
            builder.Append("- Carpetas: ").AppendLine(string.Join(", ", organization.Collections.Select(collection => SingleLine(collection.Name))));
        }
        if (assets.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Archivos");
            foreach (var asset in assets)
            {
                builder.Append("- [[assets/")
                    .Append(asset.Id.ToString("D"))
                    .Append(SafeExtension(asset.OriginalName))
                    .Append('|')
                    .Append(SingleLine(asset.OriginalName))
                    .AppendLine("]]");
            }
        }
        builder.AppendLine(ManagedEnd);
        builder.AppendLine();
        builder.AppendLine("## Notas editables");
        builder.AppendLine(NotesStart);
        builder.AppendLine(notes.Trim());
        builder.AppendLine(NotesEnd);
        return builder.ToString();
    }

    private static string? ExtractNotes(string text)
    {
        var start = text.IndexOf(NotesStart, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }
        start += NotesStart.Length;
        var end = text.IndexOf(NotesEnd, start, StringComparison.Ordinal);
        return end < 0 ? null : text[start..end].Trim();
    }

    private static bool HasFrontMatterValue(string text, string key, string value)
    {
        using var reader = new StringReader(text);
        if (reader.ReadLine() != "---")
        {
            return false;
        }
        var matches = 0;
        var expectedValue = false;
        while (reader.ReadLine() is { } line)
        {
            if (line == "---")
            {
                return matches == 1 && expectedValue;
            }
            var trimmed = line.Trim();
            if (trimmed.StartsWith($"{key}:", StringComparison.Ordinal))
            {
                matches++;
                expectedValue = string.Equals(trimmed, $"{key}: {value}", StringComparison.Ordinal);
            }
        }
        return false;
    }

    private static bool IsManagedNote(string text, Guid id)
    {
        if (!HasFrontMatterValue(text, "unishare_id", id.ToString("D")))
        {
            return false;
        }
        var previousEnd = 0;
        foreach (var marker in new[] { ManagedStart, ManagedEnd, NotesStart, NotesEnd })
        {
            var position = text.IndexOf(marker, StringComparison.Ordinal);
            if (position < previousEnd || position != text.LastIndexOf(marker, StringComparison.Ordinal))
            {
                return false;
            }
            previousEnd = position + marker.Length;
        }
        return true;
    }

    private static string ReplaceManagedBlock(string existing, string generated)
    {
        var start = existing.IndexOf(ManagedStart, StringComparison.Ordinal);
        var end = existing.IndexOf(ManagedEnd, StringComparison.Ordinal) + ManagedEnd.Length;
        var generatedStart = generated.IndexOf(ManagedStart, StringComparison.Ordinal);
        var generatedEnd = generated.IndexOf(ManagedEnd, StringComparison.Ordinal) + ManagedEnd.Length;
        return string.Concat(existing.AsSpan(0, start), generated.AsSpan(generatedStart, generatedEnd - generatedStart), existing.AsSpan(end));
    }

    private static bool HasPersonalContent(string text, Guid id)
    {
        var start = text.IndexOf(ManagedStart, StringComparison.Ordinal);
        var end = text.IndexOf(ManagedEnd, StringComparison.Ordinal) + ManagedEnd.Length;
        var notesStart = text.IndexOf(NotesStart, StringComparison.Ordinal);
        var notesEnd = text.IndexOf(NotesEnd, StringComparison.Ordinal) + NotesEnd.Length;
        var canonicalHeader = $"---\nunishare_id: {id:D}\n---";
        return !string.IsNullOrWhiteSpace(ExtractNotes(text)) ||
            !string.Equals(text[..start].ReplaceLineEndings("\n").Trim(), canonicalHeader, StringComparison.Ordinal) ||
            !string.Equals(text[end..notesStart].Trim(), "## Notas editables", StringComparison.Ordinal) ||
            !string.IsNullOrWhiteSpace(text[notesEnd..]);
    }

    private static async Task<int> RemoveStaleManagedNotesAsync(
        string notesRoot,
        string assetsRoot,
        HashSet<string> expectedNotes,
        ISet<string> referencedAssets,
        CancellationToken cancellationToken)
    {
        var preserved = 0;
        foreach (var notePath in Directory.EnumerateFiles(notesRoot, "*.md", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expectedNotes.Contains(notePath) ||
                string.Equals(Path.GetFileName(notePath), IndexFileName, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParse(Path.GetFileNameWithoutExtension(notePath), out var id))
            {
                continue;
            }

            var text = await File.ReadAllTextAsync(notePath, cancellationToken);
            if (!IsManagedNote(text, id))
            {
                continue;
            }

            CollectManagedAssetPaths(text, assetsRoot, referencedAssets);
            if (HasPersonalContent(text, id))
            {
                preserved++;
                continue;
            }
            File.Delete(notePath);
        }
        return preserved;
    }

    private static void CollectManagedAssetPaths(
        string text,
        string assetsRoot,
        ISet<string> destination)
    {
        const string prefix = "[[assets/";
        var offset = 0;
        while ((offset = text.IndexOf(prefix, offset, StringComparison.Ordinal)) >= 0)
        {
            var start = offset + prefix.Length;
            var alias = text.IndexOf('|', start);
            var closing = text.IndexOf("]]", start, StringComparison.Ordinal);
            var end = alias >= start && (closing < 0 || alias < closing) ? alias : closing;
            if (end < start)
            {
                break;
            }

            var fileName = text[start..end];
            if (string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) &&
                Guid.TryParse(Path.GetFileNameWithoutExtension(fileName), out _))
            {
                destination.Add(Path.Combine(assetsRoot, fileName));
            }
            offset = end;
        }
    }

    private static async Task<HashSet<string>> FindReferencedAssetNamesAsync(
        string vaultRoot,
        IEnumerable<string> candidateNames,
        CancellationToken cancellationToken)
    {
        var remaining = candidateNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (remaining.Count == 0)
        {
            return referenced;
        }

        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        foreach (var notePath in Directory.EnumerateFiles(vaultRoot, "*.md", enumeration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await File.ReadAllTextAsync(notePath, cancellationToken);
            foreach (var fileName in remaining.ToArray())
            {
                if (text.Contains(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    referenced.Add(fileName);
                    remaining.Remove(fileName);
                }
            }

            if (remaining.Count == 0)
            {
                break;
            }
        }

        return referenced;
    }

    private static string ValidateVaultRoot(string vaultRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultRoot);
        var root = Path.GetFullPath(vaultRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException("La carpeta de Obsidian seleccionada no existe.");
        }
        return root;
    }

    private static string SafeExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Length is > 0 and <= 16 &&
               extension.Skip(1).All(character => char.IsAsciiLetterOrDigit(character))
            ? extension.ToLowerInvariant()
            : string.Empty;
    }

    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string WikiAlias(string value) =>
        SingleLine(value).Replace("|", "-", StringComparison.Ordinal)
            .Replace("[[", "(", StringComparison.Ordinal)
            .Replace("]]", ")", StringComparison.Ordinal);

    private static async Task CopyAtomicallyAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        var temporary = destination + $".{Guid.NewGuid():N}.partial";
        try
        {
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using (var output = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static async Task<bool> MatchesAssetAsync(
        string path,
        Domain.AssetManifest asset,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != asset.ByteLength)
        {
            return false;
        }

        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        return string.Equals(hash, asset.Sha256, StringComparison.Ordinal);
    }

    private static async Task WriteAtomicallyAsync(
        string destination,
        string content,
        CancellationToken cancellationToken)
    {
        var temporary = destination + $".{Guid.NewGuid():N}.partial";
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
