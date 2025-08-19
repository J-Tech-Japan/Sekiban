using ResultBoxes;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     Orchestrates command execution including context creation, handler invocation,
///     tag reservation, and event/tag persistence.
/// </summary>
public interface ICommandExecutor
{
    /// <summary>
    ///     Executes a command with a handler function
    /// </summary>
    /// <typeparam name="TCommand">The type of command to execute</typeparam>
    /// <param name="command">The command to execute</param>
    /// <param name="handlerFunc">The handler function to process the command</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>ResultBox containing the execution result or error</returns>
    Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken = default) where TCommand : ICommand;

    /// <summary>
    ///     Executes a command that includes its own handler logic
    /// </summary>
    /// <typeparam name="TCommand">The type of command to execute</typeparam>
    /// <param name="command">The command with handler to execute</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>ResultBox containing the execution result or error</returns>
    Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand>;
}
