namespace Sekiban.Core.Exceptions;

/// <summary>
///     This exception is thrown when the argument is invalid.
///     Used generally in the library.
/// </summary>
public class SekibanInvalidArgumentException : Exception, ISekibanException
{
    public SekibanInvalidArgumentException(string message) : base(message)
    {
    }
    public SekibanInvalidArgumentException() : base(string.Empty)
    {
    }
}
