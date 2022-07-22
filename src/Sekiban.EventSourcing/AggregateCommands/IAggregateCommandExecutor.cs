namespace Sekiban.EventSourcing.AggregateCommands;

public interface IAggregateCommandExecutor
{
    /// <summary>
    ///     集約コマンドを実行する
    ///     こちらのメソッドは集約の変更機能のメソッドとなります。
    /// </summary>
    /// <param name="command">対象集約コマンド</param>
    /// <param name="callHistories">呼び出し履歴 APIなどから直接コマンドを呼ぶ場合はnullで良い。他のイベントやコマンドからコマンドを呼ぶ際に、呼出履歴をつける</param>
    /// <typeparam name="T">集約クラス</typeparam>
    /// <typeparam name="TContents">DTOクラス</typeparam>
    /// <typeparam name="C">コマンドクラス</typeparam>
    /// <returns></returns>
    Task<AggregateCommandExecutorResponse<TContents, C>> ExecChangeCommandAsync<T, TContents, C>(
        Guid aggregateId,
        C command,
        List<CallHistory>? callHistories = null) where T : TransferableAggregateBase<TContents>
        where TContents : IAggregateContents
        where C : ChangeAggregateCommandBase<T>;
    /// <summary>
    ///     集約コマンドを実行する
    ///     こちらのメソッドは集約の新規作成機能のメソッドとなります。
    /// </summary>
    /// <param name="command">対象集約コマンド</param>
    /// <param name="callHistories">呼び出し履歴 APIなどから直接コマンドを呼ぶ場合はnullで良い。他のイベントやコマンドからコマンドを呼ぶ際に、呼出履歴をつける</param>
    /// <typeparam name="T">集約クラス</typeparam>
    /// <typeparam name="TContents">DTOクラス</typeparam>
    /// <typeparam name="C">コマンドクラス</typeparam>
    /// <returns></returns>
    Task<AggregateCommandExecutorResponse<TContents, C>> ExecCreateCommandAsync<T, TContents, C>(C command, List<CallHistory>? callHistories = null)
        where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents where C : ICreateAggregateCommand<T>;
}
