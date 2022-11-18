namespace Sekiban.Core.Exceptions;

public class SekibanTypeNotFoundException : Exception, ISekibanException
{
    public SekibanTypeNotFoundException(string message) : base(message)
    {
    }
}
