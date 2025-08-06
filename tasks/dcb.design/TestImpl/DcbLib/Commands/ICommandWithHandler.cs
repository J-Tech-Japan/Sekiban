using DcbLib.Events;
using ResultBoxes;

namespace DcbLib.Commands;

/// <summary>
/// Represents a command that includes its own handler logic.
/// This combines ICommand and ICommandHandler into a single interface,
/// allowing commands to be self-contained with their processing logic.
/// </summary>
/// <typeparam name="TSelf">The type of the command itself (for CRTP pattern)</typeparam>
public interface ICommandWithHandler<TSelf> : ICommand
    where TSelf : ICommandWithHandler<TSelf>
{
    /// <summary>
    /// Handles the command execution with the provided context
    /// </summary>
    /// <param name="context">The command context providing access to tag states</param>
    /// <returns>ResultBox containing EventOrNone representing the result of handling</returns>
    Task<ResultBox<EventOrNone>> HandleAsync(ICommandContext context);
}