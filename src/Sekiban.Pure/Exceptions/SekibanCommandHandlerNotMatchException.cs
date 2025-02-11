using Sekiban.Pure.Exceptions;
namespace Sekiban.Pure.Exceptions;

public class SekibanCommandHandlerNotMatchException(string message) : ApplicationException(message), ISekibanException;