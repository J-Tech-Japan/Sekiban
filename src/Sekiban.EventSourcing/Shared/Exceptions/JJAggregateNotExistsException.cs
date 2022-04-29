namespace Sekiban.EventSourcing.Shared.Exceptions;

public class JJAggregateNotExistsException : Exception, IJJException
{
    public Guid AggregateId { get; set; }
    public string AggregateTypeName { get; set; }

    public JJAggregateNotExistsException(Guid aggregateId, string aggregateTypeName)
    {
        AggregateId = aggregateId;
        AggregateTypeName = aggregateTypeName;
    }
}
