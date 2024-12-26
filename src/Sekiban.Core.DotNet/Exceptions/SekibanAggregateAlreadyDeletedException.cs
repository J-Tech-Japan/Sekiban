using Sekiban.Core.Command;
namespace Sekiban.Core.Exceptions;

/// <summary>
///     Aggregate has been already deleted. Thus it cannot add new events except for
///     <see cref="ICancelDeletedCommand" />
/// </summary>
public class SekibanAggregateAlreadyDeletedException : Exception, ISekibanException;
