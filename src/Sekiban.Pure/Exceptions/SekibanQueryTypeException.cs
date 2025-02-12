namespace Sekiban.Pure.Exceptions;

public class SekibanQueryTypeException(string message)
    : ApplicationException(message), ISekibanException;