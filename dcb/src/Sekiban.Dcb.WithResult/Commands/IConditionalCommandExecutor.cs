using ResultBoxes;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     OPTIONAL, additive executor capability (ResultBox facade) for command execution with
///     <see cref="CommandExecutionOptions" /> — currently, conditional (unique-key) single-event append. Separate from
///     <see cref="ICommandExecutor" />/<c>ISekibanExecutor</c> so existing implementors compile untouched; feature-detect
///     with <c>is IConditionalCommandExecutor</c>. The existing <c>ExecuteAsync</c> overloads are unchanged and keep
///     default (unconditional) behaviour.
/// </summary>
public interface IConditionalCommandExecutor
{
    Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CommandExecutionOptions options,
        CancellationToken cancellationToken = default) where TCommand : ICommand;

    Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        CommandExecutionOptions options,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand>;
}
