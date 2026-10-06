namespace UniShare.Domain;

public sealed record LibraryCollectionDefinition(Guid Id, string Name, string NormalizedName)
{
    public static (string Name, string NormalizedName) Normalize(string value)
    {
        var (name, normalized) = LibraryTag.Normalize(value);
        return (name, normalized);
    }
}
