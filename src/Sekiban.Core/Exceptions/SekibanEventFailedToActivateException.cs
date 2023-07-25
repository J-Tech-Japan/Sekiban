namespace Sekiban.Core.Exceptions;

/// <summary>
///     This exception throws when event failed to add and apply to the aggregate
/// </summary>
public class SekibanEventFailedToActivateException : Exception, ISekibanException
{
}
