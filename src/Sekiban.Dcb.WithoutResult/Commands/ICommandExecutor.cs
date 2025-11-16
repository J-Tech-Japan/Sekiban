using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     Orchestrates command execution including context creation, handler invocation,
///     tag reservation, and event/tag persistence.
///     Exception-based error handling - throws exceptions on failure
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
    /// <returns>The execution result</returns>
    /// <exception cref="Exception">Thrown when execution fails</exception>
    Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<EventOrNone>> handlerFunc,
        CancellationToken cancellationToken = default) where TCommand : ICommand;

    /// <summary>
    ///     Executes a command that includes its own handler logic
    /// </summary>
    /// <typeparam name="TCommand">The type of command to execute</typeparam>
    /// <param name="command">The command with handler to execute</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>The execution result</returns>
    /// <exception cref="Exception">Thrown when execution fails</exception>
    Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand>;
}
