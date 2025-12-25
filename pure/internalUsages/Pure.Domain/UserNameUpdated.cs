using Sekiban.Pure.Events;
namespace Pure.Domain;

[GenerateSerializer]
public record UserNameUpdated(string OldName, string NewName) : IEventPayload;
