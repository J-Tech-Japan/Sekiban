using Sekiban.Pure.Events;
namespace SharedDomain.Aggregates.User.Events;

[GenerateSerializer]
public record UserNameChanged(
    [property: Id(0)]
    Guid UserId,
    [property: Id(1)]
    string NewName) : IEventPayload;
