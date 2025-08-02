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