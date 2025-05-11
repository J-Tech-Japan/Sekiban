using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;
using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Orleans.Parts;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;

namespace Sekiban.Pure.Orleans.Grains;

/// <summary>
/// Projection grain that maintains a multi‑projection state and is fed by an Orleans stream.
/// スナップショット保存は 5 分ごとのバックグラウンド‑タイマーで行う。
/// </summary>
public class MultiProjectorGrain : Grain, IMultiProjectorGrain
{
    // ---------- Tunables ----------
    private static readonly TimeSpan SafeStateWindow = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan PersistInterval = TimeSpan.FromMinutes(5);

    // ---------- State ----------
    private MultiProjectionState? _safeState;
    private MultiProjectionState? _unsafeState;

    // ---------- Infra ----------
    private readonly IPersistentState<SerializableMultiProjectionState> _persistentState;
    private readonly IEventReader _eventReader;
    private readonly SekibanDomainTypes _domainTypes;
    private readonly ILogger<MultiProjectorGrain> _logger;

    // ---------- Stream ----------
    private IAsyncStream<IEvent>? _eventStream;
    private StreamSubscriptionHandle<IEvent>? _subscription;
    private readonly List<IEvent> _buffer = new();
    private bool _bootstrapping = true;
    private bool _streamActive = false;

    // ---------- Snapshot control ----------
    private IDisposable? _persistTimer;
    private volatile bool _pendingSave;

    public MultiProjectorGrain(
        [PersistentState("multiProjector", "Default")] IPersistentState<SerializableMultiProjectionState> persistentState,
        IEventReader eventReader,
        SekibanDomainTypes domainTypes,
        ILogger<MultiProjectorGrain> logger)
    {
        _persistentState = persistentState;
        _eventReader = eventReader;
        _domainTypes = domainTypes;
        _logger = logger;
    }

    #region Activation / Deactivation

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);

        // 1) restore snapshot
        await _persistentState.ReadStateAsync(ct);
        if (_persistentState.RecordExists && _persistentState.State is not null)
        {
            var restored = await _persistentState.State.ToMultiProjectionStateAsync(_domainTypes);
            if (restored.HasValue) _safeState = restored.Value;
        }

        // 2) catch‑up
        await CatchUpFromStoreAsync();

        // 3) subscribe stream
        _eventStream = this.GetStreamProvider("EventStreamProvider").GetStream<IEvent>(StreamId.Create("AllEvents", Guid.Empty));
        _subscription = await _eventStream.SubscribeAsync(OnStreamEventAsync, OnStreamErrorAsync, OnStreamCompletedAsync);

        // 4) snapshot timer
        _persistTimer = RegisterTimer(_ => PersistTick(), null, PersistInterval, PersistInterval);

        _bootstrapping = false;
        _streamActive = true;
        FlushBuffer();
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken token)
    {
        _persistTimer?.Dispose();
        if (_subscription is not null) await _subscription.UnsubscribeAsync();
        if (_pendingSave) await PersistStateAsync(_safeState);
        _streamActive = false;
        await base.OnDeactivateAsync(reason, token);
    }

    #endregion

    #region Stream callbacks

    private Task OnStreamEventAsync(IEvent e, StreamSequenceToken? _) => Enqueue(e);

    private Task OnStreamErrorAsync(Exception ex)
    {
        _logger.LogError(ex, "Stream error");
        return Task.CompletedTask;
    }

    private Task OnStreamCompletedAsync()
    {
        _logger.LogInformation("Stream completed");
        return Task.CompletedTask;
    }

    private Task Enqueue(IEvent e)
    {
        _buffer.Add(e);
        return Task.CompletedTask;
    }

    private void InitializeState()
    {
        var projector = GetProjectorFromName();
        _safeState = new MultiProjectionState(
            projector, 
            Guid.Empty, 
            string.Empty,
            0, 
            0, 
            _safeState?.RootPartitionKey ?? "default");
    }

    private void FlushBuffer()
    {
        if (_safeState is null && _unsafeState is null)
        {
            InitializeState();
        }
        if (!_buffer.Any()) return;
        
        var projector = GetProjectorFromName();
        
        _buffer.Sort((a, b) => {
            var aValue = new SortableUniqueIdValue(a.SortableUniqueId);
            var bValue = new SortableUniqueIdValue(b.SortableUniqueId);
            return aValue.IsEarlierThan(bValue) ? -1 : (aValue.IsLaterThan(bValue) ? 1 : 0);
        });
        
        // safeBorderを計算
        var safeBorder = new SortableUniqueIdValue((DateTime.UtcNow - SafeStateWindow).ToString("O"));
        
        // safeBorderより古いイベントのインデックスを探す
        int splitIndex = _buffer.FindLastIndex(e => 
            new SortableUniqueIdValue(e.SortableUniqueId).IsEarlierThan(safeBorder));
        
        // 古いイベントがあれば処理
        if (splitIndex >= 0)
        {
            // 古いイベントを取得
            var oldEvents = _buffer.Take(splitIndex + 1).ToList();
            
            // _safeStateに適用
            var newSafeState = _domainTypes.MultiProjectorsType.Project(_safeState?.ProjectorCommon ?? projector, oldEvents).UnwrapBox();
            var lastOldEvt = oldEvents.Last();
            _safeState = new MultiProjectionState(
                newSafeState, 
                lastOldEvt.Id, 
                lastOldEvt.SortableUniqueId,
                (_safeState?.Version ?? 0) + 1, 
                0, 
                _safeState?.RootPartitionKey ?? "default");
            
            // 適用したイベントはバッファから削除
            _buffer.RemoveRange(0, splitIndex + 1);
            
            // スナップショット更新フラグをセット
            _pendingSave = true;
        }
        
        // バッファに残っているイベント（新しいイベント）があり、かつ_safeStateが初期化されていれば
        if (_buffer.Any() && _safeState != null)
        {
            // _unsafeStateを更新
            var newUnsafeState = _domainTypes.MultiProjectorsType.Project(_safeState.ProjectorCommon, _buffer).UnwrapBox();
            var lastNewEvt = _buffer.Last();
            _unsafeState = new MultiProjectionState(
                newUnsafeState,
                lastNewEvt.Id,
                lastNewEvt.SortableUniqueId,
                _safeState.Version + 1,
                0,
                _safeState.RootPartitionKey);
        } else
        {
            _unsafeState = null;
        }
        // 注意: バッファの残りのイベントは削除せず、保持しておく
    }

    #endregion

    #region State building

    private async Task CatchUpFromStoreAsync()
    {
        var projector = GetProjectorFromName();
        var lastId = _safeState?.LastSortableUniqueId ?? string.Empty;
        var retrieval = EventRetrievalInfo.All with
        {
            SortableIdCondition = string.IsNullOrEmpty(lastId)
                ? ISortableIdCondition.None
                : ISortableIdCondition.Between(
                    new SortableUniqueIdValue(lastId), 
                    new SortableUniqueIdValue(SortableUniqueIdValue.Generate(DateTime.UtcNow.AddSeconds(10), Guid.Empty)))
        };

        var events = (await _eventReader.GetEvents(retrieval)).UnwrapBox().ToList();
        if (events.Count > 0) await UpdateProjectionAsync(events, projector);
    }

    private Task ApplyEventAsync(IEvent evt) =>
        UpdateProjectionAsync(new List<IEvent> { evt }, GetProjectorFromName());

    private async Task UpdateProjectionAsync(List<IEvent> events, IMultiProjectorCommon projector)
    {
        if (!events.Any()) return;

        var safeBorder = new SortableUniqueIdValue((DateTime.UtcNow - SafeStateWindow).ToString("O"));
        var lastEvt = events.Last();
        bool allSafe = new SortableUniqueIdValue(lastEvt.SortableUniqueId).IsEarlierThan(safeBorder);

        if (allSafe)
        {
            var newState = _domainTypes.MultiProjectorsType.Project(projector, events).UnwrapBox();
            _safeState = new MultiProjectionState(newState, lastEvt.Id, lastEvt.SortableUniqueId, (_safeState?.Version ?? 0) + 1, 0, _safeState?.RootPartitionKey ?? "default");
            _unsafeState = null;
            _pendingSave = true;
        }
        else
        {
            var split = events.FindLastIndex(e => new SortableUniqueIdValue(e.SortableUniqueId).IsEarlierThan(safeBorder));
            if (split >= 0) await UpdateProjectionAsync(events.Take(split + 1).ToList(), projector);
            var newProj = _domainTypes.MultiProjectorsType.Project(projector, events.Skip(split + 1).ToList()).UnwrapBox();
            _unsafeState = new MultiProjectionState(newProj, lastEvt.Id, lastEvt.SortableUniqueId, (_safeState?.Version ?? 0) + 1, 0, _safeState?.RootPartitionKey ?? "default");
        }
    }

    private Task PersistTick()
    {
        if (!_pendingSave) return Task.CompletedTask;
        _pendingSave = false;
        _ = PersistStateAsync(_safeState).ContinueWith(t =>
        {
            if (t.IsFaulted) _logger.LogError(t.Exception, "Persist failed");
        });
        return Task.CompletedTask;
    }

    private async Task PersistStateAsync(MultiProjectionState? state)
    {
        if (state is null) return;
        _persistentState.State = await SerializableMultiProjectionState.CreateFromAsync(state, _domainTypes);
        await _persistentState.WriteStateAsync();
    }

    #endregion

    #region Public API (IMultiProjectorGrain)

    public async Task BuildStateAsync() => await BuildStateIfNeededAsync();

    public async Task RebuildStateAsync()
    {
        _safeState = null;
        _unsafeState = null;
        await CatchUpFromStoreAsync();
        _pendingSave = true;
    }

    public async Task<MultiProjectionState> GetStateAsync() =>
        _unsafeState ?? _safeState ?? await BuildStateIfNeededAsync();

    public async Task<QueryResultGeneral> QueryAsync(IQueryCommon query)
    {
        var res = await _domainTypes.QueryTypes.ExecuteAsQueryResult(query, GetProjectorForQuery, new ServiceCollection().BuildServiceProvider()) ??
            throw new ApplicationException("Query not found");
        return res.Remap(v => v.ToGeneral(query)).UnwrapBox();
    }

    public async Task<IListQueryResult> QueryAsync(IListQueryCommon query)
    {
        var res = await _domainTypes.QueryTypes.ExecuteAsQueryResult(query, GetProjectorForQuery, new ServiceCollection().BuildServiceProvider()) ??
            throw new ApplicationException("Query not found");
        return res.UnwrapBox();
    }

    public Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId)
    {
        // バッファ内に存在するか確認
        if (_buffer.Any(e => new SortableUniqueIdValue(e.SortableUniqueId).IsLaterThanOrEqual(new SortableUniqueIdValue(sortableUniqueId))))
        {
            return Task.FromResult(true);
        }
        
        // LastSortableUniqueIdが目標のIDより新しいか確認
        if (!string.IsNullOrEmpty(_safeState?.LastSortableUniqueId))
        {
            var lastId = new SortableUniqueIdValue(_safeState.LastSortableUniqueId);
            var targetId = new SortableUniqueIdValue(sortableUniqueId);
            
            if (lastId.IsLaterThanOrEqual(targetId))
            {
                return Task.FromResult(true);
            }
        }
        
        return Task.FromResult(false);
    }

    #endregion

    #region Helpers

    private IMultiProjectorCommon GetProjectorFromName() =>
        _domainTypes.MultiProjectorsType.GetProjectorFromMultiProjectorName(this.GetPrimaryKeyString());

    private async Task<MultiProjectionState> BuildStateIfNeededAsync()
    {
        if (_streamActive)
        {
            // ストリームアクティブの場合はバッファを処理
            // 新しいFlushBuffer実装により、古いイベントは_safeStateに適用され、
            // 新しいイベントは_unsafeStateに適用される
            FlushBuffer();
        }
        else
        {
            // ストリームが非アクティブの場合はイベントストアから読み込む
            await CatchUpFromStoreAsync();
        }
        return _unsafeState ?? _safeState ?? throw new InvalidOperationException("State not initialized");
    }

    private async Task<ResultBox<IMultiProjectorStateCommon>> GetProjectorForQuery(IMultiProjectionEventSelector _)
    {
        await BuildStateIfNeededAsync();
        return (_unsafeState ?? _safeState)?.ToResultBox<IMultiProjectorStateCommon>() ??
               new ApplicationException("No state available");
    }

    #endregion
}
