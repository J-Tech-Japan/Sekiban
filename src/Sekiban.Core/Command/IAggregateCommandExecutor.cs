using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.History;
namespace Sekiban.Core.Command;

public interface IAggregateCommandExecutor
{
    /// <summary>
    ///     集約コマンドを実行する
    ///     こちらのメソッドは集約の変更機能のメソッドとなります。
    /// </summary>
    /// <param name="command">対象集約コマンド</param>
    /// <param name="callHistories">呼び出し履歴 APIなどから直接コマンドを呼ぶ場合はnullで良い。他のイベントやコマンドからコマンドを呼ぶ際に、呼出履歴をつける</param>
    /// <typeparam name="TAggregatePayload">集約クラス</typeparam>
    /// <typeparam name="C">コマンドクラス</typeparam>
    /// <returns></returns>
    Task<(AggregateCommandExecutorResponse, List<IAggregateEvent>)> ExecChangeCommandAsync<TAggregatePayload, C>(
        C command,
        List<CallHistory>? callHistories = null)
        where TAggregatePayload : IAggregatePayload, new()
        where C : ChangeAggregateCommandBase<TAggregatePayload>;

    /// <summary>
    ///     集約コマンドを実行する
    ///     こちらのメソッドは集約の変更機能のメソッドとなります。
    /// </summary>
    /// <param name="command">対象集約コマンド</param>
    /// <param name="callHistories">呼び出し履歴 APIなどから直接コマンドを呼ぶ場合はnullで良い。他のイベントやコマンドからコマンドを呼ぶ際に、呼出履歴をつける</param>
    /// <typeparam name="TAggregatePayload">集約クラス</typeparam>
    /// <typeparam name="C">コマンドクラス</typeparam>
    /// <returns></returns>
    Task<(AggregateCommandExecutorResponse, List<IAggregateEvent>)> ExecChangeCommandWithoutValidationAsync<TAggregatePayload, C>(
        C command,
        List<CallHistory>? callHistories = null)
        where TAggregatePayload : IAggregatePayload, new()
        where C : ChangeAggregateCommandBase<TAggregatePayload>;
    /// <summary>
    ///     集約コマンドを実行する
    ///     こちらのメソッドは集約の新規作成機能のメソッドとなります。
    /// </summary>
    /// <param name="command">対象集約コマンド</param>
    /// <param name="callHistories">呼び出し履歴 APIなどから直接コマンドを呼ぶ場合はnullで良い。他のイベントやコマンドからコマンドを呼ぶ際に、呼出履歴をつける</param>
    /// <typeparam name="T">集約クラス</typeparam>
    /// <typeparam name="TAggregatePayload">集約クラス</typeparam>
    /// <typeparam name="C">コマンドクラス</typeparam>
    /// <returns></returns>
    Task<(AggregateCommandExecutorResponse, List<IAggregateEvent>)> ExecCreateCommandAsync<TAggregatePayload, C>(
        C command,
        List<CallHistory>? callHistories = null)
        where TAggregatePayload : IAggregatePayload, new()
        where C : ICreateAggregateCommand<TAggregatePayload>;
    /// <summary>
    ///     集約コマンドを実行する
    ///     こちらのメソッドは集約の新規作成機能のメソッドとなります。
    /// </summary>
    /// <param name="command">対象集約コマンド</param>
    /// <param name="callHistories">呼び出し履歴 APIなどから直接コマンドを呼ぶ場合はnullで良い。他のイベントやコマンドからコマンドを呼ぶ際に、呼出履歴をつける</param>
    /// <typeparam name="T">集約クラス</typeparam>
    /// <typeparam name="TAggregatePayload">DTOクラス</typeparam>
    /// <typeparam name="C">コマンドクラス</typeparam>
    /// <returns></returns>
    Task<(AggregateCommandExecutorResponse, List<IAggregateEvent>)> ExecCreateCommandWithoutValidationAsync<TAggregatePayload, C>(
        C command,
        List<CallHistory>? callHistories = null)
        where TAggregatePayload : IAggregatePayload, new()
        where C : ICreateAggregateCommand<TAggregatePayload>;
}
