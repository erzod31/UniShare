using System.Security.Cryptography;
using UniShare.Application;

namespace UniShare.Infrastructure;

public sealed class FileBlobStore : IBlobStore
{
    private readonly string _profileRoot;
    private readonly string _blobRoot;
    private readonly string _stagingRoot;

    public FileBlobStore(string profileRoot)
    {
        _profileRoot = Path.GetFullPath(profileRoot);
        _blobRoot = Path.Combine(_profileRoot, "blobs");
        _stagingRoot = Path.Combine(_profileRoot, "staging");
        Directory.CreateDirectory(_blobRoot);
        Directory.CreateDirectory(_stagingRoot);
    }

    public async Task<BlobImportResult> ImportFileAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var sourceInfo = new FileInfo(fullSourcePath);
        if (!sourceInfo.Exists)
        {
            throw new FileNotFoundException("El archivo seleccionado ya no existe.", fullSourcePath);
        }

        if ((sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("No se importan enlaces simbólicos o puntos de reanálisis.");
        }

        var stagingPath = Path.Combine(_stagingRoot, $"{Guid.NewGuid():N}.partial");
        try
        {
            long bytesWritten = 0;
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = new FileStream(
                fullSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hasher.AppendData(buffer, 0, read);
                    bytesWritten += read;
                }

                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            var hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            var detectedMimeType = await ContentTypeDetector.DetectFileAsync(stagingPath, cancellationToken);
            var relativePath = $"blobs/{hash[..2]}/{hash.Substring(2, 2)}/{hash}";
            var destination = GetAbsolutePath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var alreadyExisted = File.Exists(destination);

            if (alreadyExisted)
            {
                if (!await VerifyFileAsync(destination, hash, cancellationToken))
                {
                    throw new IOException("El almacén contiene un blob dañado con el mismo nombre de hash.");
                }
            }
            else
            {
                try
                {
                    File.Move(stagingPath, destination);
                }
                catch (IOException) when (File.Exists(destination))
                {
                    alreadyExisted = true;
                    if (!await VerifyFileAsync(destination, hash, cancellationToken))
                    {
                        throw;
                    }
                }
            }

            return new BlobImportResult(
                hash,
                bytesWritten,
                relativePath,
                sourceInfo.Name,
                detectedMimeType ?? MimeTypes.FromFileName(sourceInfo.Name),
                alreadyExisted);
        }
        finally
        {
            if (File.Exists(stagingPath))
            {
                File.Delete(stagingPath);
            }
        }
    }

    public Task<bool> VerifyAsync(string sha256, CancellationToken cancellationToken = default)
    {
        ValidateHash(sha256);
        var relativePath = $"blobs/{sha256[..2]}/{sha256.Substring(2, 2)}/{sha256}";
        var absolutePath = GetAbsolutePath(relativePath);
        return File.Exists(absolutePath)
            ? VerifyFileAsync(absolutePath, sha256, cancellationToken)
            : Task.FromResult(false);
    }

    public string GetAbsolutePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("La ruta de un blob debe ser relativa.", nameof(relativePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_profileRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = _profileRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _profileRoot
            : _profileRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("La ruta sale del perfil de UniShare.");
        }

        return fullPath;
    }

    private static async Task<bool> VerifyFileAsync(
        string path,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actual),
            Convert.FromHexString(expectedHash));
    }

    private static void ValidateHash(string hash)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("SHA-256 inválido.", nameof(hash));
        }
    }
}

