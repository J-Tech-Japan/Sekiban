namespace Sekiban.Core.Exceptions;

public class SekibanEventRetrievalException(string message) : Exception(message), ISekibanException;
