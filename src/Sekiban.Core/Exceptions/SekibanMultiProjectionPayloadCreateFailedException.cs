namespace Sekiban.Core.Exceptions;

public class SekibanMultiProjectionPayloadCreateFailedException : Exception, ISekibanException
{
    public string AggregateTypeName { get; }

    public SekibanMultiProjectionPayloadCreateFailedException(string aggregateTypeName) : base($"Aggregate {aggregateTypeName} failed to create.") =>
        AggregateTypeName = aggregateTypeName;
}
