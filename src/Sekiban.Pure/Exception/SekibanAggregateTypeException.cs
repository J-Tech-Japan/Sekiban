using Sekiban.Core.Exceptions;
namespace Sekiban.Pure.Exception;

public class SekibanAggregateTypeException(string message) : ApplicationException(message), ISekibanException
{
}
public class SekibanEventTypeNotFoundException(string message) : ApplicationException(message), ISekibanException
{
}
