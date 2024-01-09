namespace Sekiban.Core.Exceptions;

public class SekibanSerializerException(string message) : Exception(message), ISekibanException;
