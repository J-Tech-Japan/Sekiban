namespace Sekiban.EventSourcing.Shared.Exceptions;

public class JJAggregateCommandInconsistentVersionException : Exception, IJJException
{
    public Guid AggregateId { get; set; }
    public int CorrectVersion { get; set; }

    public JJAggregateCommandInconsistentVersionException(Guid aggregateId, int correctVersion)
    {
        AggregateId = aggregateId;
        CorrectVersion = correctVersion;
    }
}
