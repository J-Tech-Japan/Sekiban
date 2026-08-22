using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     OPTIONAL, additive executor capability (exception-throwing facade) for command execution with
///     <see cref="CommandExecutionOptions" /> — conditional (unique-key) single-event append and/or the additive
///     PostgreSQL expected-tag-position protocol. Separate from
///     <see cref="ICommandExecutor" />/<c>ISekibanExecutor</c> so existing implementors compile untouched; feature-detect
///     with <c>is IConditionalCommandExecutor</c>. A typed failure (KeyReuseConflict / ConditionNotSupported) is thrown
///     through the guarded boundary, preserving the original exception.
/// </summary>
public interface IConditionalCommandExecutor
{
    Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<EventOrNone>> handlerFunc,
        CommandExecutionOptions options,
        CancellationToken cancellationToken = default) where TCommand : ICommand;

    Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        CommandExecutionOptions options,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand>;
}
