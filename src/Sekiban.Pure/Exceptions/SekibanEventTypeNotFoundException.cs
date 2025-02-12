using Sekiban.Pure.Exceptions;
namespace Sekiban.Pure.Exceptions;

public class SekibanEventTypeNotFoundException(string message) : ApplicationException(message), ISekibanException;
