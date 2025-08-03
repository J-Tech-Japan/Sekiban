using DcbLib.Events;
using ResultBoxes;

namespace DcbLib.Commands;

/// <summary>
/// Handles commands of a specific type.
/// Handlers contain the business logic for processing commands.
/// </summary>
/// <typeparam name="TCommand">The type of command this handler processes</typeparam>
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    /// <summary>
    /// Processes the command and returns the result containing an event with tags or none
    /// </summary>
    /// <param name="command">The command to process</param>
    /// <param name="context">The command context providing access to current state</param>
    /// <returns>ResultBox containing EventOrNone (event with tags) or error information</returns>
    Task<ResultBox<EventOrNone>> HandleAsync(TCommand command, ICommandContext context);
}