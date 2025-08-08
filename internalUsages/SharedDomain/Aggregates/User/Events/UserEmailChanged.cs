using Sekiban.Pure.Events;
namespace SharedDomain.Aggregates.User.Events;

[GenerateSerializer]
public record UserEmailChanged(
    [property: Id(0)] Guid UserId,
    [property: Id(1)] string NewEmail) : IEventPayload;