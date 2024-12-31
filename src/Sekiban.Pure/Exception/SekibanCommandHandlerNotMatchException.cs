using Sekiban.Core.Exceptions;
namespace Sekiban.Pure.Exception;

public class SekibanCommandHandlerNotMatchException(string message) : ApplicationException(message), ISekibanException;
