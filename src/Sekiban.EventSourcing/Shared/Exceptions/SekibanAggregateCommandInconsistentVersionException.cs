namespace Sekiban.EventSourcing.Shared.Exceptions;

public class SekibanAggregateCommandInconsistentVersionException : Exception, ISekibanException
{
    public Guid AggregateId { get; set; }
    public int CorrectVersion { get; set; }

    public SekibanAggregateCommandInconsistentVersionException(Guid aggregateId, int correctVersion)
    {
        AggregateId = aggregateId;
        CorrectVersion = correctVersion;
    }
}