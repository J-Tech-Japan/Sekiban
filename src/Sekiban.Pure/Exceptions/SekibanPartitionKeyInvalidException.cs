namespace Sekiban.Pure.Exceptions;

public class SekibanPartitionKeyInvalidException(string message) : ApplicationException(message), ISekibanException;
