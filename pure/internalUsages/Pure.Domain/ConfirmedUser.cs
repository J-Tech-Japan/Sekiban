using Sekiban.Pure.Aggregates;
namespace Pure.Domain;

public record ConfirmedUser(string Name, string Email) : IAggregatePayload;
