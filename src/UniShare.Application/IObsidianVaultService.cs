namespace UniShare.Application;

public interface IObsidianVaultService
{
    Task<ObsidianExportResult> ExportAsync(
        string vaultRoot,
        CancellationToken cancellationToken = default);

    Task<ObsidianImportResult> ImportNotesAsync(
        string vaultRoot,
        CancellationToken cancellationToken = default);
}

public sealed record ObsidianExportResult(int NoteCount, int AssetCount, int PreservedNoteCount);

public sealed record ObsidianImportResult(int UpdatedItemCount, int UnchangedItemCount, int IgnoredFileCount);
