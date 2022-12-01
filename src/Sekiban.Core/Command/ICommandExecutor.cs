using System.Collections.Immutable;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.History;

namespace Sekiban.Core.Command;

public interface ICommandExecutor
{
    /// <summary>
    ///     集約コマンドを実行する
    ///     こちらのメソッドは集約の新規作成機能のメソッドとなります。
    /// </summary>
    /// <param name="command">対象集約コマンド</param>
    /// <param name="events">作成されたイベント</param>
    /// <param name="callHistories">呼び出し履歴 APIなどから直接コマンドを呼ぶ場合はnullで良い。他のイベントやコマンドからコマンドを呼ぶ際に、呼出履歴をつける</param>
    /// <typeparam name="TCommand">コマンドクラス</typeparam>
    /// <returns></returns>
    Task<CommandExecutorResponse> ExecCommandAsync<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null)
        where TCommand : ICommandCommon;

    Task<CommandExecutorResponseWithEvents> ExecCommandWithEventsAsync<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null)
        where TCommand : ICommandCommon;
    /// <summary>
    ///     集約コマンドを実行する
    ///     こちらのメソッドは集約の新規作成機能のメソッドとなります。
    /// </summary>
    /// <param name="command">対象集約コマンド</param>
    /// <param name="events">作成されたイベント</param>
    /// <param name="callHistories">呼び出し履歴 APIなどから直接コマンドを呼ぶ場合はnullで良い。他のイベントやコマンドからコマンドを呼ぶ際に、呼出履歴をつける</param>
    /// <typeparam name="TCommand">コマンドクラス</typeparam>
    /// <returns></returns>
    Task<CommandExecutorResponse> ExecCommandWithoutValidationAsync<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null)
        where TCommand : ICommandCommon;

    Task<CommandExecutorResponseWithEvents> ExecCommandWithoutValidationWithEventsAsync<TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null)
        where TCommand : ICommandCommon;

}
