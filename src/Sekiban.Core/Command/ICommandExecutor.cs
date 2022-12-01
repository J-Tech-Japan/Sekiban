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
    /// <param name="callHistories">呼び出し履歴 APIなどから直接コマンドを呼ぶ場合はnullで良い。他のイベントやコマンドからコマンドを呼ぶ際に、呼出履歴をつける</param>
    /// <typeparam name="TAggregatePayload">集約クラス</typeparam>
    /// <typeparam name="TCommand">コマンドクラス</typeparam>
    /// <returns></returns>
    Task<(CommandExecutorResponse, List<IEvent>)> ExecCommandAsync<TAggregatePayload, TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null)
        where TAggregatePayload : IAggregatePayload, new()
        where TCommand : ICommand<TAggregatePayload>;

    /// <summary>
    ///     集約コマンドを実行する
    ///     こちらのメソッドは集約の新規作成機能のメソッドとなります。
    /// </summary>
    /// <param name="command">対象集約コマンド</param>
    /// <param name="callHistories">呼び出し履歴 APIなどから直接コマンドを呼ぶ場合はnullで良い。他のイベントやコマンドからコマンドを呼ぶ際に、呼出履歴をつける</param>
    /// <typeparam name="TAggregatePayload">Payloadクラス</typeparam>
    /// <typeparam name="TCommand">コマンドクラス</typeparam>
    /// <returns></returns>
    Task<(CommandExecutorResponse, List<IEvent>)> ExecCommandWithoutValidationAsync<TAggregatePayload, TCommand>(
        TCommand command,
        List<CallHistory>? callHistories = null)
        where TAggregatePayload : IAggregatePayload, new()
        where TCommand : ICommand<TAggregatePayload>;
}
