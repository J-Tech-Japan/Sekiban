namespace Sekiban.Pure.Exception;

public class SekibanAggregateTypeRestrictionException(string message)
    : ApplicationException(message), ISekibanException;
