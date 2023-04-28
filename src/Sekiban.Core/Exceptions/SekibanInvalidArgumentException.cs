namespace Sekiban.Core.Exceptions;

public class SekibanInvalidArgumentException : Exception, ISekibanException
{
    public SekibanInvalidArgumentException(string message) : base(message) { }
    public SekibanInvalidArgumentException() : base(string.Empty) { }
}
