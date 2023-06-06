using Sekiban.Core.History;
namespace Sekiban.Core.Command;

/// <summary>
///     Command Execution Interface
///     Application Developer can use this interface to execute a command
/// </summary>
public interface ICommandExecutor
{
    /// <summary>
    ///     Execute a command (basic)
    ///     This method will validate the command and execute it
    ///     CommandExecutorResponse does not contains produced events
    /// </summary>
    /// <param name="command">Aggregate Command</param>
    /// <param name="callHistories">Attaching Command History : ev.GetCallHistoriesIncludesItself() can create current history</param>
    /// <typeparam name="TCommand">Command should imprelemt <see cref="ICommandCommon" /></typeparam>
    /// <returns>Executed command response.</returns>
    Task<CommandExecutorResponse> ExecCommandAsync<TCommand>(TCommand command, List<CallHistory>? callHistories = null)
        where TCommand : ICommandCommon;

    /// <summary>
    ///     Execute a command (basic)
    ///     This method will validate the command and execute it
    ///     CommandExecutorResponse contains produced events
    /// </summary>
    /// <param name="command">Aggregate Command</param>
    /// <param name="callHistories">Attaching Command History : ev.GetCallHistoriesIncludesItself() can create current history</param>
    /// <typeparam name="TCommand">Command should imprelemt <see cref="ICommandCommon" /></typeparam>
    /// <returns>Executed command response.</returns>
    Task<CommandExecutorResponseWithEvents> ExecCommandWithEventsAsync<TCommand>(TCommand command, List<CallHistory>? callHistories = null)
        where TCommand : ICommandCommon;
    /// <summary>
    ///     Execute a command (basic)
    ///     This method will NOT validate the command and execute it
    ///     CommandExecutorResponse does not contains produced events
    /// </summary>
    /// <param name="command">Aggregate Command</param>
    /// <param name="callHistories">Attaching Command History : ev.GetCallHistoriesIncludesItself() can create current history</param>
    /// <typeparam name="TCommand">Command should imprelemt <see cref="ICommandCommon" /></typeparam>
    /// <returns>Executed command response.</returns>
    Task<CommandExecutorResponse> ExecCommandWithoutValidationAsync<TCommand>(TCommand command, List<CallHistory>? callHistories = null)
        where TCommand : ICommandCommon;

    /// <summary>
    ///     Execute a command (basic)
    ///     This method will NOT validate the command and execute it
    ///     CommandExecutorResponse contains produced events
    /// </summary>
    /// <param name="command">Aggregate Command</param>
    /// <param name="callHistories">Attaching Command History : ev.GetCallHistoriesIncludesItself() can create current history</param>
    /// <typeparam name="TCommand">Command should imprelemt <see cref="ICommandCommon" /></typeparam>
    /// <returns>Executed command response.</returns>
    Task<CommandExecutorResponseWithEvents> ExecCommandWithoutValidationWithEventsAsync<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null) where TCommand : ICommandCommon;
}
