using Sekiban.Pure;
namespace Pure.Domain;

public record ConfirmedUser(string Name, string Email) : IAggregatePayload;
