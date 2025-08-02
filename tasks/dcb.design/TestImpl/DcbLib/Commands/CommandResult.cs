using DcbLib.Events;
using DcbLib.Tags;

namespace DcbLib.Commands;

/// <summary>
/// Result of command processing
/// </summary>
public record CommandResult
{
    /// <summary>
    /// Successful result with tags and events
    /// </summary>
    public record Success(
        IReadOnlyList<ITag> Tags,
        IReadOnlyList<IEventPayload> Events,
        Dictionary<string, object>? Metadata = null
    ) : CommandResult;

    /// <summary>
    /// Validation failure result
    /// </summary>
    public record ValidationFailure(
        IReadOnlyList<string> Errors,
        Dictionary<string, IReadOnlyList<string>>? FieldErrors = null
    ) : CommandResult;

    /// <summary>
    /// Business rule violation result
    /// </summary>
    public record BusinessRuleViolation(
        string Code,
        string Message,
        Dictionary<string, object>? Details = null
    ) : CommandResult;

    /// <summary>
    /// Concurrency conflict result
    /// </summary>
    public record ConcurrencyConflict(
        IReadOnlyList<ITag> ConflictedTags,
        string Message = "One or more entities were modified by another operation"
    ) : CommandResult;

    private CommandResult() { }
}