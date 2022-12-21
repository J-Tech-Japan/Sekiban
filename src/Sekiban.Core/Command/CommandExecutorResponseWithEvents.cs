using Sekiban.Core.Event;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Core.Command;

/// <summary>
///     Command Response Data object wit executed event
/// </summary>
/// <param name="AggregateId">Aggregate Id(Null if it has validation error)</param>
/// <param name="CommandId">Command Id(Null if it has validation error)</param>
/// <param name="Version">Version(Null if it has validation error)</param>
/// <param name="ValidationResults">Validate Results (Null if it passes validation)</param>
/// <param name="Events">Produced Events(Empty if it has validation error)</param>
/// <param name="LastSortableUniqueId">Last SortableUniqueId(Null if it has validation error)</param>
public record CommandExecutorResponseWithEvents(
    Guid? AggregateId,
    Guid? CommandId,
    int Version,
    IEnumerable<ValidationResult>? ValidationResults,
    ImmutableList<IEvent> Events,
    string? LastSortableUniqueId) : CommandExecutorResponse(AggregateId, CommandId, Version, ValidationResults, LastSortableUniqueId)
{
    public CommandExecutorResponseWithEvents() : this(
        null,
        null,
        0,
        null,
        ImmutableList<IEvent>.Empty,
        null)
    {
    }
    public CommandExecutorResponseWithEvents(CommandExecutorResponse response, ImmutableList<IEvent> events) : this(
        response.AggregateId,
        response.CommandId,
        response.Version,
        response.ValidationResults,
        events,
        response.LastSortableUniqueId)
    {
    }
}
