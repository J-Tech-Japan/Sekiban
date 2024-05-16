namespace Sekiban.Core.Exceptions;

public class SekibanCommandInvalidAggregateException(Guid? commandId) : Exception($"CommandId: {commandId}"), ISekibanException;
