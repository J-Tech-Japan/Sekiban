namespace Sekiban.EventSourcing.Shared.Exceptions;

public class SekibanAggregateNotExistsException : Exception, ISekibanException
{
    public Guid AggregateId { get; set; }
    public string AggregateTypeName { get; set; }

    public SekibanAggregateNotExistsException(Guid aggregateId, string aggregateTypeName)
    {
        AggregateId = aggregateId;
        AggregateTypeName = aggregateTypeName;
    }
}
