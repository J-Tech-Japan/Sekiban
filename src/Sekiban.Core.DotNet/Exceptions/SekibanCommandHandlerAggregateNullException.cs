namespace Sekiban.Core.Exceptions;

/// <summary>
///     This exception throws when developer try to retrieve event from aggregate which is not exists.
/// </summary>
public class SekibanCommandHandlerAggregateNullException : Exception, ISekibanException;
