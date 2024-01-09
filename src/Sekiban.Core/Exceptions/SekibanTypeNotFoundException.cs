namespace Sekiban.Core.Exceptions;

/// <summary>
///     This exception is thrown when the type is not found.
/// </summary>
public class SekibanTypeNotFoundException(string message) : Exception(message), ISekibanException;
