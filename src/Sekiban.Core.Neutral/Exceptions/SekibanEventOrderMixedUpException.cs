namespace Sekiban.Core.Exceptions;

/// <summary>
///     This exception is thrown when the event order is mixed up.
/// </summary>
public class SekibanEventOrderMixedUpException : ApplicationException, ISekibanException;
