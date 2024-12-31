using Sekiban.Pure;
namespace Pure.Domain;

public record Branch(string Name) : IAggregatePayload;
