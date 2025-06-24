using Sekiban.Pure.Events;

namespace DaprSample.Domain.User;

public record UserCreated(
    Guid UserId,
    string Name,
    string Email) : IEventPayload;

public record UserNameChanged(
    Guid UserId,
    string NewName) : IEventPayload;

public record UserEmailChanged(
    Guid UserId,
    string NewEmail) : IEventPayload;