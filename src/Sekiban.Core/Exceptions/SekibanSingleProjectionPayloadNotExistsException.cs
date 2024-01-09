namespace Sekiban.Core.Exceptions;

public class SekibanSingleProjectionPayloadNotExistsException(string message) : Exception(message), ISekibanException;
