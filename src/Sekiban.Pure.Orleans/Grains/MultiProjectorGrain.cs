using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
namespace Sekiban.Pure.Orleans.Grains;

public class MultiProjectorGrain : Grain, IMultiProjectorGrain
{
    private static readonly TimeSpan SafeStateTime = TimeSpan.FromSeconds(7);
    private MultiProjectionState? UnsafeState { get; set; }
    private readonly IPersistentState<MultiProjectionState> safeState;
    private readonly IEventReader eventReader;
    private readonly SekibanDomainTypes sekibanDomainTypes;

    public MultiProjectorGrain(
        [PersistentState("multiProjector", "Default")]
        IPersistentState<MultiProjectionState> safeState,
        IEventReader eventReader,
        SekibanDomainTypes sekibanDomainTypes)
    {
        this.safeState = safeState;
        this.eventReader = eventReader;
        this.sekibanDomainTypes = sekibanDomainTypes;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await safeState.ReadStateAsync();
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    public async Task RebuildStateAsync()
    {
        // 初回または完全再構築時は、プロジェクターはグレイン名から取得する
        var projector = GetProjectorFromMultiProjectorName();
        // 全イベントを取得
        var events = (await eventReader.GetEvents(EventRetrievalInfo.All)).UnwrapBox().ToList();
        if (!events.Any())
        {
            safeState.State = new MultiProjectionState(projector, Guid.Empty, string.Empty, 0, 0, "default");
            return;
        }
        await UpdateProjectionAsync(events, projector);
    }

    public async Task BuildStateAsync()
    {
        if (!safeState.RecordExists)
        {
            await RebuildStateAsync();
            return;
        }

        // 以前の安全状態の最後の SortableUniqueId をチェックポイントとして利用
        var checkpoint = safeState.State.LastSortableUniqueId;
        var retrievalInfo = EventRetrievalInfo.All with
        {
            SortableIdCondition = ISortableIdCondition.Since(new SortableUniqueIdValue(checkpoint))
        };

        var events = (await eventReader.GetEvents(retrievalInfo)).UnwrapBox().ToList();
        if (!events.Any())
        {
            return;
        }

        // 既存の安全状態に基づくプロジェクターを利用
        var projector = safeState.State.ProjectorCommon;
        await UpdateProjectionAsync(events, projector);
    }

    /// <summary>
    ///     取得したイベント群を安全／不安全に分割し、状態を更新する共通処理
    /// </summary>
    private async Task UpdateProjectionAsync(List<IEvent> events, IMultiProjectorCommon projector)
    {
        var currentTime = DateTime.UtcNow;
        var safeTimeThreshold = currentTime.Subtract(SafeStateTime);
        // 安全判定のため、しっかりとしたタイムスタンプ形式で比較
        var safeThresholdSortable = new SortableUniqueIdValue(safeTimeThreshold.ToString("O"));
        var eventsList = events.ToList();
        var lastEvent = eventsList.Last();
        var lastEventSortable = new SortableUniqueIdValue(lastEvent.SortableUniqueId);

        if (lastEventSortable.IsEarlierThan(safeThresholdSortable))
        {
            // 全てのイベントが安全と判断できる場合
            var projectedState = sekibanDomainTypes.MultiProjectorsType.Project(projector, eventsList).UnwrapBox();
            safeState.State = new MultiProjectionState(
                projectedState,
                lastEvent.Id,
                lastEvent.SortableUniqueId,
                (safeState.State?.Version ?? 0) + 1,
                0,
                safeState.State?.RootPartitionKey ?? "default");
            UnsafeState = null;
            await safeState.WriteStateAsync();
        } else
        {
            // 安全と判断できるイベントと、最新の（不安全な）イベントの分割点を検出
            var splitIndex = eventsList.FindLastIndex(
                e =>
                    new SortableUniqueIdValue(e.SortableUniqueId).IsEarlierThan(safeThresholdSortable));

            if (splitIndex >= 0)
            {
                var safeEvents = eventsList.Take(splitIndex + 1).ToList();
                var lastSafeEvent = safeEvents.Last();
                var safeProjectedState
                    = sekibanDomainTypes.MultiProjectorsType.Project(projector, safeEvents).UnwrapBox();
                safeState.State = new MultiProjectionState(
                    safeProjectedState,
                    lastSafeEvent.Id,
                    lastSafeEvent.SortableUniqueId,
                    (safeState.State?.Version ?? 0) + 1,
                    0,
                    safeState.State?.RootPartitionKey ?? "default");
                await safeState.WriteStateAsync();
            }

            // 不安全な最新イベントを既存の安全状態に適用して更新
            var unsafeEvents = eventsList.Skip(Math.Max(splitIndex + 1, 0)).ToList();
            var unsafeProjectedState = sekibanDomainTypes
                .MultiProjectorsType
                .Project(safeState.State.ProjectorCommon, unsafeEvents)
                .UnwrapBox();
            UnsafeState = new MultiProjectionState(
                unsafeProjectedState,
                lastEvent.Id,
                lastEvent.SortableUniqueId,
                safeState.State.Version + 1,
                0,
                safeState.State.RootPartitionKey);
        }
    }

    public async Task<MultiProjectionState> GetStateAsync()
    {
        await BuildStateAsync();
        return UnsafeState ?? safeState.State;
    }

    public async Task<QueryResultGeneral> QueryAsync(IQueryCommon query)
    {
        var result = await sekibanDomainTypes.QueryTypes.ExecuteAsQueryResult(
                query,
                GetProjectorForQuery,
                new ServiceCollection().BuildServiceProvider()) ??
            throw new ApplicationException("Query not found");
        return result
            .Remap(value => value.ToGeneral(query))
            .UnwrapBox();
    }

    public async Task<IListQueryResult> QueryAsync(IListQueryCommon query)
    {
        var result = await sekibanDomainTypes.QueryTypes.ExecuteAsQueryResult(
                query,
                GetProjectorForQuery,
                new ServiceCollection().BuildServiceProvider()) ??
            throw new ApplicationException("Query not found");
        return result.UnwrapBox();
    }

    public async Task<ResultBox<IMultiProjectorStateCommon>> GetProjectorForQuery(
        IMultiProjectionEventSelector multiProjectionEventSelector)
    {
        await BuildStateAsync();
        return UnsafeState?.ToResultBox<IMultiProjectorStateCommon>() ??
            safeState?.State.ToResultBox<IMultiProjectorStateCommon>() ??
            new ApplicationException("No state found");
    }

    public IMultiProjectorCommon GetProjectorFromMultiProjectorName()
    {
        var grainName = this.GetPrimaryKeyString();
        return sekibanDomainTypes.MultiProjectorsType.GetProjectorFromMultiProjectorName(grainName);
    }
}