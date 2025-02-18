using Sekiban.Pure.Events;
namespace Sekiban.Pure.Orleans;

public interface IAggregateEventHandlerGrain : IGrainWithStringKey
{
    /// <summary>
    ///     指定された lastSortableUniqueId を前提に、新しいイベントを保存する。
    ///     楽観的排他を行い、手元の LastSortableUniqueId と異なる場合はエラーや差分を返す。
    ///     成功時には新たな LastSortableUniqueId を返却する。
    /// </summary>
    /// <param name="expectedLastSortableUniqueId">Projector側が認識している最後の SortableUniqueId</param>
    /// <param name="newEvents">作成されたイベント一覧</param>
    /// <returns>
    ///     (成功時) 新たな LastSortableUniqueId
    ///     (失敗時) 例外をスローする or もしくは差分イベントを返す別パターンなど
    /// </returns>
    Task<IReadOnlyList<IEvent>> AppendEventsAsync(
        string expectedLastSortableUniqueId,
        IReadOnlyList<IEvent> newEvents
    );

    /// <summary>
    ///     イベントの差分を取得する。
    /// </summary>
    /// <param name="fromSortableUniqueId">差分の取得開始点となる SortableUniqueId</param>
    /// <param name="limit">取得する最大件数(必要なら)</param>
    /// <returns>該当するイベント一覧</returns>
    Task<IReadOnlyList<IEvent>> GetDeltaEventsAsync(
        string fromSortableUniqueId,
        int? limit = null
    );

    /// <summary>
    ///     全イベントを最初から取得する。
    ///     プロジェクタのバージョン変更などで State を作り直す際に利用。
    ///     取得件数が大きい場合はページングなども検討。
    /// </summary>
    /// <returns>全イベント一覧</returns>
    Task<IReadOnlyList<IEvent>> GetAllEventsAsync();

    /// <summary>
    ///     現在の管理している最後の SortableUniqueId を返す。
    /// </summary>
    /// <returns>最後の SortableUniqueId</returns>
    Task<string> GetLastSortableUniqueIdAsync();

    /// <summary>
    ///     指定のプロジェクターを登録しておく(任意)。
    ///     複数 Projector がある場合、差分取得の最終取得位置を記録する仕組みなども考えられる。
    /// </summary>
    /// <param name="projectorKey">プロジェクターの固有キー</param>
    /// <returns></returns>
    Task RegisterProjectorAsync(string projectorKey);
}