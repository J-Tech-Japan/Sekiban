namespace Sekiban.Core.Exceptions;

public class SekibanAggregateNotExistsException : Exception, ISekibanException
{
    public SekibanAggregateNotExistsException(Guid aggregateId, string aggregateTypeName)
    {
        AggregateId = aggregateId;
        AggregateTypeName = aggregateTypeName;
    }

    public Guid AggregateId { get; set; }
    public string AggregateTypeName { get; set; }
}
