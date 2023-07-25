namespace Sekiban.Core.Exceptions;

/// <summary>
///     Exception throws when developer try to retrieve aggregate which is not exists.
/// </summary>
public class SekibanAggregateNotExistsException : Exception, ISekibanException
{

    public Guid AggregateId { get; set; }
    public string AggregateTypeName { get; set; }
    public SekibanAggregateNotExistsException(Guid aggregateId, string aggregateTypeName, string rootPartitionKey)
    {
        AggregateId = aggregateId;
        AggregateTypeName = aggregateTypeName;
    }
}
