namespace Sekiban.Core.Exceptions;

/// <summary>
///     This exception is thrown when the event is not implemented to the aggregate.
/// </summary>
public class SekibanEventNotImplementedException : Exception, ISekibanException
{
    public SekibanEventNotImplementedException(string message) : base(message)
    {
    }
}
