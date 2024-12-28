namespace Sekiban.Core.Exceptions;

public class SekibanMultiProjectionPayloadNotExistsException(string message) : Exception(message), ISekibanException;
