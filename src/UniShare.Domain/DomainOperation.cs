namespace UniShare.Domain;

public sealed record DomainOperation(
    Guid OperationId,
    Guid OriginDeviceId,
    long OriginCounter,
    Guid EntityId,
    string EntityType,
    string OperationType,
    int SchemaVersion,
    string Payload,
    string CausalContext,
    DateTimeOffset CreatedAt);

