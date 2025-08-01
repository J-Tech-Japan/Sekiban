using DcbLib.Events;
using DcbLib.Tags;

namespace DcbLib.Commands;

/// <summary>
/// Handles commands of a specific type.
/// Handlers contain the business logic for processing commands.
/// </summary>
/// <typeparam name="TCommand">The type of command this handler processes</typeparam>
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    /// <summary>
    /// Processes the command and returns the result containing tags and events
    /// </summary>
    /// <param name="command">The command to process</param>
    /// <param name="context">The command context providing access to current state</param>
    /// <returns>The command result with tags and events, or validation errors</returns>
    Task<CommandResult> HandleAsync(TCommand command, ICommandContext context);
}

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