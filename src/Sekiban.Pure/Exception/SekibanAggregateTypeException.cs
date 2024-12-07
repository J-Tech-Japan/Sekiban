using Sekiban.Core.Exceptions;
namespace Sekiban.Pure.Exception;

public class SekibanAggregateTypeException(string message) : ApplicationException(message), ISekibanException;
public class SekibanAggregateTypeRestrictionException(string message)
    : ApplicationException(message), ISekibanException;
public class SekibanEventTypeNotFoundException(string message) : ApplicationException(message), ISekibanException;
public class SekibanCommandHandlerNotMatchException(string message) : ApplicationException(message), ISekibanException;
public class SekibanCommandExecutionException(string message) : ApplicationException(message), ISekibanException;
