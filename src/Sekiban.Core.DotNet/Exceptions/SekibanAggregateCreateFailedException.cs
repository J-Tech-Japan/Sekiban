namespace Sekiban.Core.Exceptions;

public class SekibanAggregateCreateFailedException : Exception, ISekibanException
{
    public string AggregateTypeName { get; }

    public SekibanAggregateCreateFailedException(string aggregateTypeName) : base(
        $"Aggregate {aggregateTypeName} failed to create.") =>
        AggregateTypeName = aggregateTypeName;
}
