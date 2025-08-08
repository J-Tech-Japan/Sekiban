using Sekiban.Pure.Aggregates;
namespace SharedDomain.Aggregates.User;

[GenerateSerializer]
public record User(
    [property: Id(0)] Guid UserId,
    [property: Id(1)] string Name,
    [property: Id(2)] string Email) : IAggregatePayload;