namespace Sekiban.Core.Exceptions;

/// <summary>
///     This exception is thrown when the event is duplicated.
///     Example : Old event is accidentally add to the store.
/// </summary>
public class SekibanEventDuplicateException : Exception, ISekibanException
{
}
