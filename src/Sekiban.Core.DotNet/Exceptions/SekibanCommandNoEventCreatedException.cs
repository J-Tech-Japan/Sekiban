namespace Sekiban.Core.Exceptions;

public class SekibanCommandNoEventCreatedException(Guid? aggregateId, Guid? commandId)
    : Exception($"AggregateId:{aggregateId} CommandId:{commandId}"), ISekibanException
{
}
