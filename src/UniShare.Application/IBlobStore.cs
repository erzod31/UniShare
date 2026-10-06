namespace UniShare.Application;

public interface IBlobStore
{
    Task<BlobImportResult> ImportFileAsync(string sourcePath, CancellationToken cancellationToken = default);

    Task<bool> VerifyAsync(string sha256, CancellationToken cancellationToken = default);

    string GetAbsolutePath(string relativePath);
}

public sealed record BlobImportResult(
    string Sha256,
    long ByteLength,
    string RelativePath,
    string OriginalName,
    string MimeType,
    bool AlreadyExisted);

