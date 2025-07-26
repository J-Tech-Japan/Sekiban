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
/// Snapshot saving is performed by a background timer every 5 minutes.
/// </summary>
public class MultiProjectorGrain : Grain, IMultiProjectorGrain, ILifecycleParticipant<IGrainLifecycle>
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

    private void LogState()
    {
        // State logging removed for production use
    }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);

        await _persistentState.ReadStateAsync(ct);
        if (_persistentState.RecordExists && _persistentState.State is not null)
        {
            var restored = await _persistentState.State.ToMultiProjectionStateAsync(_domainTypes);
            if (restored.HasValue) _safeState = restored.Value;
            LogState();
        }

        // 2) catch‑up
        await CatchUpFromStoreAsync();
        LogState();

        // 4) snapshot timer
        
        _persistTimer = this.RegisterGrainTimer(_ => PersistTick(), PersistInterval, PersistInterval);
        
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken token)
    {
        _persistTimer?.Dispose();
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
        return Task.CompletedTask;
    }

    private Task Enqueue(IEvent e)
    {
        if (!_buffer.Any(existingEvent => existingEvent.SortableUniqueId == e.SortableUniqueId))
        {
            _buffer.Add(e);
        }
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
        LogState();

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
        
        var safeBorderId = SortableUniqueIdValue.Generate((DateTime.UtcNow - SafeStateWindow), Guid.Empty);
        var safeBorder = new SortableUniqueIdValue(safeBorderId);
        int splitIndex = _buffer.FindLastIndex(e => 
            new SortableUniqueIdValue(e.SortableUniqueId).IsEarlierThan(safeBorder));
        
        if (splitIndex >= 0)
        {
            var sortableUniqueIdFrom = _safeState?.GetLastSortableUniqueId() ?? SortableUniqueIdValue.MinValue;   
            var oldEvents = _buffer.Take(splitIndex + 1).Where(e => (new SortableUniqueIdValue(e.SortableUniqueId)).IsLaterThan(sortableUniqueIdFrom)).ToList();
            
            var newSafeState = _domainTypes.MultiProjectorsType.Project(_safeState?.ProjectorCommon ?? projector, oldEvents).UnwrapBox();
            var lastOldEvt = oldEvents.Last();
            _safeState = new MultiProjectionState(
                newSafeState, 
                lastOldEvt.Id, 
                lastOldEvt.SortableUniqueId,
                (_safeState?.Version ?? 0) + 1, 
                0, 
                _safeState?.RootPartitionKey ?? "default");
            
            _buffer.RemoveRange(0, splitIndex + 1);
            
            _pendingSave = true;
        }


        if (_buffer.Any() && _safeState != null)
        {
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
        LogState();
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
        LogState();
        if (events.Count > 0)
        {
            foreach (var e in events)
            {
                if (!_buffer.Any(existingEvent => existingEvent.SortableUniqueId == e.SortableUniqueId))
                {
                    _buffer.Add(e);
                }
            }
            FlushBuffer();
        }
        LogState();
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
        if (state.Version == 0) return;
        if (_persistentState.State is null || _persistentState.State.Version != state.Version)
        {
            _persistentState.State = await SerializableMultiProjectionState.CreateFromAsync(state, _domainTypes);
            await _persistentState.WriteStateAsync();
        }
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
        if (_buffer.Any(e => new SortableUniqueIdValue(e.SortableUniqueId).IsLaterThanOrEqual(new SortableUniqueIdValue(sortableUniqueId))))
        {
            return Task.FromResult(true);
        }
        
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
            FlushBuffer();
        }
        else
        {
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

    #region ILifecycleParticipant implementation
    
    /// <summary>
    /// Method to participate in the grain lifecycle.
    /// Registers a custom stage to be executed after the Activate stage.
    /// </summary>
    /// <param name="lifecycle">Grain lifecycle</param>
    public void Participate(IGrainLifecycle lifecycle)
    {
        var stage = GrainLifecycleStage.Activate + 100;
        lifecycle.Subscribe(this.GetType().FullName!, stage, InitStreamsAsync, CloseStreamsAsync);
    }
    
    /// <summary>
    /// Method to initialize the stream.
    /// Executed after the Activate stage.
    /// </summary>
    private async Task InitStreamsAsync(CancellationToken ct)
    {
        _eventStream = this.GetStreamProvider("EventStreamProvider").GetStream<IEvent>(StreamId.Create("AllEvents", Guid.Empty));
        _subscription = await _eventStream.SubscribeAsync(OnStreamEventAsync, OnStreamErrorAsync, OnStreamCompletedAsync);
        
        _streamActive = true;
        FlushBuffer();
    }
    
    /// <summary>
    /// Method to clean up the stream.
    /// Executed when the grain is deactivated.
    /// </summary>
    private Task CloseStreamsAsync(CancellationToken ct)
    {
        return _subscription?.UnsubscribeAsync() ?? Task.CompletedTask;
    }
    
    #endregion
}
