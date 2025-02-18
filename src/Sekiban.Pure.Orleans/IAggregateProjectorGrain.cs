using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Orleans.Surrogates;
namespace Sekiban.Pure.Orleans;

public interface IAggregateProjectorGrain : IGrainWithStringKey
{
    /// <summary>
    ///     現在の状態を取得する。
    ///     Stateが未作成の場合や、Projectorのバージョンが変わっている場合などは、
    ///     イベントを一括取得して再構築することを考慮。
    /// </summary>
    /// <returns>現在の集約状態</returns>
    Task<Aggregate> GetStateAsync();

    /// <summary>
    ///     コマンドを実行するエントリポイント。
    ///     現在の状態をベースに CommandHandler を使ってイベントを生成し、AggregateEventHandler へ送る。
    /// </summary>
    /// <param name="command">実行するコマンド</param>
    /// <param name="metadata">Command Metadata</param>
    /// <returns>実行後の状態や生成イベントなど、必要に応じて返す</returns>
    Task<CommandResponse> ExecuteCommandAsync(ICommandWithHandlerSerializable command, CommandMetadata metadata);

    /// <summary>
    ///     State を一から再構築する(バージョンアップ時や State 破損時など)。
    ///     すべてのイベントを AggregateEventHandler から受け取り、Projector ロジックを通して再構成。
    /// </summary>
    /// <returns>再構築後の新しい状態</returns>
    Task<Aggregate> RebuildStateAsync();
}