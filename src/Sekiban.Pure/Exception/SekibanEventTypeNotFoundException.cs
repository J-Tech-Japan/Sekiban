using Sekiban.Core.Exceptions;
namespace Sekiban.Pure.Exception;

public class SekibanEventTypeNotFoundException(string message) : ApplicationException(message), ISekibanException;
