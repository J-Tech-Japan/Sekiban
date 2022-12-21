using Sekiban.Core.Event;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

/// <summary>
///     System use command response
///     Application Developer usually don't need to use this class
/// </summary>
/// <param name="AggregateId">Aggregate Id</param>
/// <param name="Events">Events that produced in the command</param>
/// <param name="Version">Aggregate Version</param>
/// <param name="LastSortableUniqueId">Last Event SortableUniqueId</param>
public record CommandResponse(Guid AggregateId, ImmutableList<IEvent> Events, int Version, string? LastSortableUniqueId)
{
}
