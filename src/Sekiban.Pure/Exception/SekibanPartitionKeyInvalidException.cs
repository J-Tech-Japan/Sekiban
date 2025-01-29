namespace Sekiban.Pure.Exception;

public class SekibanPartitionKeyInvalidException(string message) : ApplicationException(message), ISekibanException;