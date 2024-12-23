namespace Sekiban.Core.Exceptions;

/// <summary>
///     This exception throws when command handler does not match
/// </summary>
public class SekibanCommandHandlerNotMatchException : Exception, ISekibanException
{
    public SekibanCommandHandlerNotMatchException(string message) : base(message)
    {
    }
}
