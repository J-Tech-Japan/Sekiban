namespace Sekiban.Core.Exceptions;

public class SekibanCanNotRetrieveEventBecauseOriginalTypeIsNotAggregatePayloadException : Exception, ISekibanException
{
    public SekibanCanNotRetrieveEventBecauseOriginalTypeIsNotAggregatePayloadException(string message) : base(message) { }
}
