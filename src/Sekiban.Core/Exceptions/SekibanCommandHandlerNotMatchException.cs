namespace Sekiban.Core.Exceptions;

public class SekibanCommandHandlerNotMatchException : Exception, ISekibanException
{
    public SekibanCommandHandlerNotMatchException(string message) : base(message)
    {
    }
}
