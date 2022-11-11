namespace Sekiban.Core.Exceptions;

public class SekibanEventNotImplementedException : Exception, ISekibanException
{
    public SekibanEventNotImplementedException(string message) : base(message)
    {
    }
}
