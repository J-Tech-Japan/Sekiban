namespace Sekiban.Pure.Events;

public record AggregateGroupStream(
    string AggregateGroup) : IAggregatesStream
{
    public List<string> GetStreamNames() => [AggregateGroup];
}