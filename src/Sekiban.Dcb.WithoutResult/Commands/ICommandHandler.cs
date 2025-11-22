using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     Handles commands of a specific type.
///     Handlers contain the business logic for processing commands.
///     Exception-based error handling - throws exceptions on failure
/// </summary>
/// <typeparam name="TCommand">The type of command this handler processes</typeparam>
public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    /// <summary>
    ///     Processes the command and returns the result containing an event with tags or none
    /// </summary>
    /// <param name="command">The command to process</param>
    /// <param name="context">The command context providing access to current state</param>
    /// <returns>EventOrNone (event with tags)</returns>
    /// <exception cref="Exception">Thrown when command processing fails</exception>
    static abstract Task<EventOrNone> HandleAsync(TCommand command, ICommandContext context);
}
