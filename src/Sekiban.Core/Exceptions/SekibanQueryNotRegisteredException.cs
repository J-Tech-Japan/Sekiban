namespace Sekiban.Core.Exceptions;

public class SekibanQueryNotRegisteredException : Exception, ISekibanException
{
    public SekibanQueryNotRegisteredException(string message) : base(message)
    {
    }
}
