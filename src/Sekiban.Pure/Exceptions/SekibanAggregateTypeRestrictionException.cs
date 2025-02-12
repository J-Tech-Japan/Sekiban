namespace Sekiban.Pure.Exceptions;

public class SekibanAggregateTypeRestrictionException(string message)
    : ApplicationException(message), ISekibanException;