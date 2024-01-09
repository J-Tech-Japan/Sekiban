namespace Sekiban.Core.Exceptions;

public class SekibanQueryExecutionException(string message) : Exception(message), ISekibanException;
