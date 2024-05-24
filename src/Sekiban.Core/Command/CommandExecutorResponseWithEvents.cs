using ResultBoxes;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
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
    string? LastSortableUniqueId,
    string AggregatePayloadOutTypeName,
    int EventCount)
{
    public CommandExecutorResponseWithEvents(CommandExecutorResponse response, ImmutableList<IEvent> events) : this(
        response.AggregateId,
        response.CommandId,
        response.Version,
        response.ValidationResults,
        events,
        response.LastSortableUniqueId,
        response.AggregatePayloadOutTypeName,
        events.Count)
    {
    }

    public ResultBox<CommandExecutorResponseWithEvents> ValidateAggregateId() =>
        AggregateId switch
        {
            _ when AggregateId == Guid.Empty => ResultBox<CommandExecutorResponseWithEvents>.FromException(
                new SekibanCommandInvalidAggregateException(CommandId)),
            not null => ResultBox.FromValue(this),
            _ => ResultBox<CommandExecutorResponseWithEvents>.FromException(new SekibanCommandInvalidAggregateException(CommandId))
        };
    public ResultBox<Guid> GetAggregateId() =>
        AggregateId switch
        {
            _ when AggregateId == Guid.Empty => ResultBox<Guid>.FromException(new SekibanCommandInvalidAggregateException(CommandId)),
            { } id => ResultBox.FromValue(id),
            _ => ResultBox<Guid>.FromException(new SekibanCommandInvalidAggregateException(CommandId))
        };
    public ResultBox<CommandExecutorResponseWithEvents> ValidateEventCreated() =>
        EventCount == 0
            ? ResultBox<CommandExecutorResponseWithEvents>.FromException(new SekibanCommandNoEventCreatedException(AggregateId, CommandId))
            : ResultBox.FromValue(this);
}
