namespace Sekiban.Core.Exceptions;

public class SekibanQueryNotRegisteredException(string message) : Exception(message), ISekibanException;
