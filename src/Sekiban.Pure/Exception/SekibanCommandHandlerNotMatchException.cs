namespace Sekiban.Pure.Exception;

public class SekibanCommandHandlerNotMatchException(string message) : ApplicationException(message), ISekibanException;
public class SekibanPartitionKeyInvalidException(string message) : ApplicationException(message), ISekibanException;
