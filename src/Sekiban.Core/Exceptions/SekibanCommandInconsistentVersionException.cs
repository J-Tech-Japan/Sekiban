namespace Sekiban.Core.Exceptions;

public class SekibanCommandInconsistentVersionException : Exception, ISekibanException
{

    public SekibanCommandInconsistentVersionException(Guid aggregateId, int passed, int correctVersion) : base(
        $"for aggregate {aggregateId} : {passed} was passed but should be {correctVersion}")
    {
        AggregateId = aggregateId;
        CorrectVersion = correctVersion;
    }
    public Guid AggregateId { get; set; }
    public int CorrectVersion { get; set; }
}
