using System.Text;

namespace UniShare.Domain;

public sealed record LibraryTag(Guid Id, string Name, string NormalizedName)
{
    public static (string Name, string NormalizedName) Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var displayName = string.Join(' ', value.Normalize(NormalizationForm.FormKC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (displayName.Length > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Una etiqueta no puede superar 100 caracteres.");
        }

        return (displayName, displayName.ToLowerInvariant());
    }
}

