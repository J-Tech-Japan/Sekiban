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
    string AggregatePayloadOutTypeName)
{
    public CommandExecutorResponse() : this(
        null,
        null,
        0,
        null,
        null,
        string.Empty)
    {
    }
}
