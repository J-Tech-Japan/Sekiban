using ResultBoxes;
using Sekiban.Core.Exceptions;
using System.ComponentModel.DataAnnotations;
namespace Sekiban.Core.Command;

/// <summary>
///     Command Response Data object
/// </summary>
/// <param name="AggregateId">Aggregate Id(Null if it has validation error)</param>
/// <param name="CommandId">Command Id(Null if it has validation error)</param>
/// <param name="Version">Version(Null if it has validation error)</param>
/// <param name="ValidationResults">Validate Results (Null if it passes validation)</param>
/// <param name="LastSortableUniqueId">Last SortableUniqueId(Null if it has validation error)</param>
public record CommandExecutorResponse(
    Guid? AggregateId,
    Guid? CommandId,
    int Version,
    IEnumerable<ValidationResult>? ValidationResults,
    string? LastSortableUniqueId,
    string AggregatePayloadOutTypeName,
    int EventCount)
{
    public ResultBox<CommandExecutorResponse> ValidateAggregateId() =>
        AggregateId switch
        {
            _ when AggregateId == Guid.Empty => ResultBox<CommandExecutorResponse>.FromException(
                new SekibanCommandInvalidAggregateException(CommandId)),
            not null => ResultBox.FromValue(this),
            _ => ResultBox<CommandExecutorResponse>.FromException(new SekibanCommandInvalidAggregateException(CommandId))
        };
    public ResultBox<Guid> GetAggregateId() =>
        AggregateId switch
        {
            _ when AggregateId == Guid.Empty => ResultBox<Guid>.FromException(new SekibanCommandInvalidAggregateException(CommandId)),
            { } id => ResultBox.FromValue(id),
            _ => ResultBox<Guid>.FromException(new SekibanCommandInvalidAggregateException(CommandId))
        };
    public ResultBox<CommandExecutorResponse> ValidateEventCreated() =>
        EventCount == 0
            ? ResultBox<CommandExecutorResponse>.FromException(new SekibanCommandNoEventCreatedException(AggregateId, CommandId))
            : ResultBox.FromValue(this);
}
