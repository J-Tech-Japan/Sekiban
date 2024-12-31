namespace Sekiban.Pure.Aggregates;

public record EmptyAggregatePayload : IAggregatePayload
{
    public static EmptyAggregatePayload Empty => new();
}
