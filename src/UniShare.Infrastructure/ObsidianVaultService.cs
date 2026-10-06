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

        var items = await repository.GetAllAsync(includeDeleted: false, cancellationToken);
        var assetCount = 0;
        var preservedNotes = 0;
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var notePath = Path.Combine(notesRoot, $"{item.Id:D}.md");
            var existingNotes = File.Exists(notePath)
                ? ExtractNotes(await File.ReadAllTextAsync(notePath, cancellationToken))
                : null;
            if (existingNotes is not null)
            {
                preservedNotes++;
            }

            var organization = await repository.GetOrganizationAsync(item.Id, cancellationToken);
            var assets = await repository.GetAssetsAsync(item.Id, cancellationToken);
            foreach (var asset in assets)
            {
                var extension = SafeExtension(asset.OriginalName);
                var destinationName = $"{asset.Id:D}{extension}";
                var destination = Path.Combine(assetsRoot, destinationName);
                var source = blobStore.GetAbsolutePath(asset.RelativePath);
                if (!File.Exists(destination))
                {
                    await CopyAtomicallyAsync(source, destination, cancellationToken);
                }

                assetCount++;
            }

            var text = RenderNote(
                item,
                organization,
                assets,
                existingNotes ?? item.Description ?? string.Empty);
            await WriteAtomicallyAsync(notePath, text, cancellationToken);
        }

        await WriteAtomicallyAsync(
            Path.Combine(notesRoot, IndexFileName),
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
            var notes = ExtractNotes(await File.ReadAllTextAsync(notePath, cancellationToken));
            if (item is null || notes is null)
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
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
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
