namespace Sekiban.Core.Exceptions;

/// <summary>
///     Exception throws when developer try to retrieve aggregate which is not exists.
/// </summary>
public class SekibanAggregateNotExistsException : Exception, ISekibanException
{

    public Guid AggregateId { get; }
    public string AggregateTypeName { get; }
    public string RootPartitionKey { get; }
    public SekibanAggregateNotExistsException(Guid aggregateId, string aggregateTypeName, string rootPartitionKey) :
        base($"Aggregate {aggregateTypeName} with id {aggregateId} in Root Partition {rootPartitionKey} not exists.")
    {
        AggregateId = aggregateId;
        AggregateTypeName = aggregateTypeName;
        RootPartitionKey = rootPartitionKey;
    }
}
