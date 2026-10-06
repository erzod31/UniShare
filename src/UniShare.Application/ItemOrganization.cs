using UniShare.Domain;

namespace UniShare.Application;

public sealed record ItemOrganization(
    IReadOnlyList<LibraryTag> Tags,
    IReadOnlyList<LibraryCollectionDefinition> Collections);

