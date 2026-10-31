using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UniShare.Application;

namespace UniShare.Infrastructure;

/// <summary>Discardable local reading views, separate from domain data, backup and synchronization.</summary>
public sealed class DescriptionTranslationCache(string root)
{
    public const string EngineVersion = "bergamot4-models3-policy1";

    public static string Key(string text, string target)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (target is not ("en" or "es") || text.Length > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{EngineVersion}\0{target}\0{text}")));
    }

    public async Task<DescriptionTranslationResult?> ReadAsync(string text, string target, CancellationToken token = default)
    {
        var path = Path.Combine(root, Key(text, target) + ".json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 1_600_000)
            {
                return null;
            }
            var result = JsonSerializer.Deserialize<DescriptionTranslationResult>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false));
            return IsValid(result, target) ? result : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public async Task WriteAsync(string text, DescriptionTranslationResult result, CancellationToken token = default)
    {
        if (!IsValid(result, result.Target))
        {
            return;
        }
        var path = Path.Combine(root, Key(text, result.Target) + ".json");
        Directory.CreateDirectory(root);
        var partial = path + $".{Guid.NewGuid():N}.partial";
        try
        {
            await File.WriteAllTextAsync(partial, JsonSerializer.Serialize(result), token).ConfigureAwait(false);
            File.Move(partial, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }
    }

    private static bool IsValid(DescriptionTranslationResult? result, string target) =>
        result is { Text.Length: <= 400_000 } && result.Target == target &&
        result.Status is "translated" or "original" or "unsupported" &&
        result.Source is { Length: <= 16 };
}
