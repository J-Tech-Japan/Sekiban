namespace Sekiban.Pure.Exception;

public class SekibanAggregateTypeException(string message) : ApplicationException(message), ISekibanException;
