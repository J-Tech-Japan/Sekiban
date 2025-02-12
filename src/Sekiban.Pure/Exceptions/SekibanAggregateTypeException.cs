using Sekiban.Pure.Exceptions;
namespace Sekiban.Pure.Exceptions;

public class SekibanAggregateTypeException(string message) : ApplicationException(message), ISekibanException;

