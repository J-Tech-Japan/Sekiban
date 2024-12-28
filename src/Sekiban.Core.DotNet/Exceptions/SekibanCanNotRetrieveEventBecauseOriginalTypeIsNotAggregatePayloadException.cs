namespace Sekiban.Core.Exceptions;

/// <summary>
///     This exception throws when developer try to retrieve event from aggregate which is not exists.
///     Check Aggregate Payload is a valid class.
/// </summary>
public class SekibanCanNotRetrieveEventBecauseOriginalTypeIsNotAggregatePayloadException : Exception, ISekibanException
{
    public SekibanCanNotRetrieveEventBecauseOriginalTypeIsNotAggregatePayloadException(string message) : base(message)
    {
    }
}
