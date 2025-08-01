using ResultBoxes;

namespace DcbLib.Commands;

/// <summary>
/// Orchestrates command execution including context creation, handler invocation,
/// tag reservation, and event/tag persistence.
/// </summary>
public interface ICommandExecutor
{
    /// <summary>
    /// Executes a command through the complete processing pipeline
    /// </summary>
    /// <typeparam name="TCommand">The type of command to execute</typeparam>
    /// <param name="command">The command to execute</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>ResultBox containing the execution result or error</returns>
    Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand;

    /// <summary>
    /// Executes a command with a specific handler
    /// </summary>
    /// <typeparam name="TCommand">The type of command to execute</typeparam>
    /// <param name="command">The command to execute</param>
    /// <param name="handler">The handler to use for processing</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>ResultBox containing the execution result or error</returns>
    Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command, 
        ICommandHandler<TCommand> handler,
        CancellationToken cancellationToken = default)
        where TCommand : ICommand;
}