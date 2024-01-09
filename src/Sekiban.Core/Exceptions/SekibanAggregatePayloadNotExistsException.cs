namespace Sekiban.Core.Exceptions;

public class SekibanAggregatePayloadNotExistsException(string message) : Exception(message), ISekibanException;
