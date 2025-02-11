namespace Sekiban.Pure.OrleansEventSourcing;

public record AggregateGroupStream(
    string AggregateGroup) : IAggregatesStream
{
    public List<string> GetStreamNames() => [AggregateGroup];
}