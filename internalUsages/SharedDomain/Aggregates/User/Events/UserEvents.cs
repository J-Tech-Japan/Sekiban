using Orleans;
using Sekiban.Pure.Events;

namespace SharedDomain.Aggregates.User.Events;

[GenerateSerializer]
public record UserCreated(
    [property: Id(0)] Guid UserId,
    [property: Id(1)] string Name,
    [property: Id(2)] string Email) : IEventPayload;

[GenerateSerializer]
public record UserNameChanged(
    [property: Id(0)] Guid UserId,
    [property: Id(1)] string NewName) : IEventPayload;

[GenerateSerializer]
public record UserEmailChanged(
    [property: Id(0)] Guid UserId,
    [property: Id(1)] string NewEmail) : IEventPayload;