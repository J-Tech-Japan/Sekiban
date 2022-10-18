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
    Task<(AggregateCommandExecutorResponse, List<IAggregateEvent>)> ExecChangeCommandAsync<T, TContents, C>(
        C command,
        List<CallHistory>? callHistories = null) where T : AggregateBase<TContents>
        where TContents : IAggregateContents, new()
        where C : ChangeAggregateCommandBase<T>;

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
    Task<(AggregateCommandExecutorResponse, List<IAggregateEvent>)> ExecChangeCommandWithoutValidationAsync<T, TContents, C>(
        C command,
        List<CallHistory>? callHistories = null) where T : AggregateBase<TContents>
        where TContents : IAggregateContents, new()
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
    Task<(AggregateCommandExecutorResponse, List<IAggregateEvent>)> ExecCreateCommandAsync<T, TContents, C>(
        C command,
        List<CallHistory>? callHistories = null) where T : AggregateBase<TContents>, new()
        where TContents : IAggregateContents, new()
        where C : ICreateAggregateCommand<T>;
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
    Task<(AggregateCommandExecutorResponse, List<IAggregateEvent>)> ExecCreateCommandWithoutValidationAsync<T, TContents, C>(
        C command,
        List<CallHistory>? callHistories = null) where T : AggregateBase<TContents>, new()
        where TContents : IAggregateContents, new()
        where C : ICreateAggregateCommand<T>;
}
