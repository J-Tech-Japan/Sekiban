using Dapr.Actors;
using Dapr.Actors.Runtime;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Pure.Dapr.Configuration;
using Sekiban.Pure.Dapr.Parts;
using Sekiban.Pure.Dapr.Serialization;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Exceptions;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
using Sekiban.Pure.Repositories;
using Sekiban.Pure;
using Microsoft.Extensions.DependencyInjection;

namespace Sekiban.Pure.Dapr.Actors;

[Actor(TypeName = nameof(MultiProjectorActor))]
public class MultiProjectorActor : Actor, IMultiProjectorActor, IRemindable
{
    // ---------- Tunables ----------
    private static readonly TimeSpan SafeStateWindow = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan PersistInterval = TimeSpan.FromMinutes(5);
    
    // ---------- State ----------
    private MultiProjectionState? _safeState;
    private MultiProjectionState? _unsafeState;
    
    // ---------- Infra ----------
    private readonly IEventReader _eventReader;
    private readonly SekibanDomainTypes _domainTypes;
    private readonly IDaprSerializationService _serialization;
    private readonly DaprClient _daprClient;
    private readonly DaprSekibanOptions _options;
    private readonly ILogger<MultiProjectorActor> _logger;
    
    // ---------- Event Buffer ----------
    private readonly List<IEvent> _buffer = new();
    private bool _bootstrapping = true;
    private bool _pendingSave = false;
    
    // ---------- State Keys ----------
    private const string StateKey = "multiprojector_state";
    private const string SnapshotReminderName = "snapshot_reminder";

    public MultiProjectorActor(
        ActorHost host,
        IEventReader eventReader,
        SekibanDomainTypes domainTypes,
        IDaprSerializationService serialization,
        DaprClient daprClient,
        IOptions<DaprSekibanOptions> options,
        ILogger<MultiProjectorActor> logger) : base(host)
    {
        _eventReader = eventReader ?? throw new ArgumentNullException(nameof(eventReader));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Activation / Deactivation

    protected override async Task OnActivateAsync()
    {
        await base.OnActivateAsync();
        
        _logger.LogInformation("MultiProjectorActor {ActorId} activated", Id.GetId());
        
        try
        {
            // Register reminder for periodic snapshot saving
            await RegisterReminderAsync(
                SnapshotReminderName,
                null,
                TimeSpan.FromMinutes(1), // Initial delay
                PersistInterval);
                
            _logger.LogInformation("Snapshot reminder registered successfully for {ActorId}", Id.GetId());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register reminder for {ActorId}. Falling back to timer-based approach.", Id.GetId());
            
            // Fallback to timer if reminder fails
            await RegisterTimerAsync(
                "SnapshotTimer",
                nameof(HandleSnapshotTimerAsync),
                Array.Empty<byte>(),
                TimeSpan.FromMinutes(1),
                PersistInterval);
        }
        
        // Initial state loading and catch-up will be done via EnsureStateLoadedAsync
        // when the first event arrives via PubSub
        _bootstrapping = false;
    }

    protected override async Task OnDeactivateAsync()
    {
        _logger.LogInformation("MultiProjectorActor {ActorId} deactivating", Id.GetId());
        
        // Save pending state if any
        if (_pendingSave && _safeState != null)
        {
            await PersistStateAsync(_safeState);
        }
        
        // Unregister snapshot reminder
        await UnregisterReminderAsync(SnapshotReminderName);
        
        await base.OnDeactivateAsync();
    }

    #endregion

    #region IRemindable Implementation

    public async Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
    {
        switch (reminderName)
        {
            case SnapshotReminderName:
                await HandleSnapshotReminder();
                break;
            default:
                _logger.LogWarning("Unknown reminder: {ReminderName}", reminderName);
                break;
        }
    }

    private async Task HandleSnapshotReminder()
    {
        if (!_pendingSave || _safeState == null) return;
        
        _pendingSave = false;
        await PersistStateAsync(_safeState);
    }

    #endregion

    #region Timer Fallback Methods

    /// <summary>
    /// Timer fallback for snapshot handling when reminders are not available
    /// </summary>
    public async Task HandleSnapshotTimerAsync(byte[] state)
    {
        await HandleSnapshotReminder();
    }

    #endregion

    #region State Management

    private async Task EnsureStateLoadedAsync()
    {
        if (_safeState != null) return;
        
        _logger.LogInformation("Loading snapshot for MultiProjectorActor {ActorId}", Id.GetId());
        
        var savedState = await StateManager.TryGetStateAsync<SerializableMultiProjectionState>(StateKey);
        if (savedState.HasValue && savedState.Value != null)
        {
            var restored = await savedState.Value.ToMultiProjectionStateAsync(_domainTypes);
            if (restored.HasValue)
            {
                _safeState = restored.Value;
                LogState();
            }
        }
        
        // Catch up from store
        await CatchUpFromStoreAsync();
    }

    private void LogState()
    {
        _logger.LogInformation("SafeState {SafeState}", _safeState?.ProjectorCommon.GetType().Name ?? "null");
        _logger.LogInformation("SafeState Version {Version}", _safeState?.Version.ToString() ?? "0");
        
        if (_safeState?.ProjectorCommon is IAggregateListProjectorAccessor accessor)
        {
            _logger.LogInformation("SafeState list count {Count}", accessor.GetAggregates().Count);
        }
        
        _logger.LogInformation("UnsafeState {UnsafeState}", _unsafeState?.ProjectorCommon.GetType().Name ?? "null");
        _logger.LogInformation("UnsafeState Version {Version}", _unsafeState?.Version.ToString() ?? "0");
        
        if (_unsafeState?.ProjectorCommon is IAggregateListProjectorAccessor accessorUnsafe)
        {
            _logger.LogInformation("UnsafeState list count {Count}", accessorUnsafe.GetAggregates().Count);
        }
    }

    private async Task CatchUpFromStoreAsync()
    {
        var lastId = _safeState?.LastSortableUniqueId ?? string.Empty;
        var retrieval = EventRetrievalInfo.All with
        {
            SortableIdCondition = string.IsNullOrEmpty(lastId)
                ? ISortableIdCondition.None
                : ISortableIdCondition.Between(
                    new SortableUniqueIdValue(lastId), 
                    new SortableUniqueIdValue(SortableUniqueIdValue.Generate(DateTime.UtcNow.AddSeconds(10), Guid.Empty)))
        };
        
        var eventsResult = await _eventReader.GetEvents(retrieval);
        if (!eventsResult.IsSuccess) return;
        
        var events = eventsResult.GetValue().ToList();
        _logger.LogInformation("Catch Up Starting Events {count} events", events.Count);
        
        if (events.Count > 0)
        {
            // Add events to buffer with duplicate check
            foreach (var e in events)
            {
                if (!_buffer.Any(existingEvent => existingEvent.SortableUniqueId == e.SortableUniqueId))
                {
                    _buffer.Add(e);
                }
            }
            FlushBuffer();
        }
        
        _logger.LogInformation("Catch Up Finished {count} events", events.Count);
        LogState();
    }

    private void FlushBuffer()
    {
        _logger.LogInformation("Start flush buffer {count} events", _buffer.Count);
        LogState();

        if (_safeState == null && _unsafeState == null)
        {
            InitializeState();
        }
        
        if (!_buffer.Any()) return;
        
        var projector = GetProjectorFromName();
        
        // Sort buffer by sortable unique ID
        _buffer.Sort((a, b) => {
            var aValue = new SortableUniqueIdValue(a.SortableUniqueId);
            var bValue = new SortableUniqueIdValue(b.SortableUniqueId);
            return aValue.IsEarlierThan(bValue) ? -1 : (aValue.IsLaterThan(bValue) ? 1 : 0);
        });
        
        // Calculate safe border
        var safeBorderId = SortableUniqueIdValue.Generate((DateTime.UtcNow - SafeStateWindow), Guid.Empty);
        var safeBorder = new SortableUniqueIdValue(safeBorderId);
        
        // Find split index
        int splitIndex = _buffer.FindLastIndex(e => 
            new SortableUniqueIdValue(e.SortableUniqueId).IsEarlierThan(safeBorder));
        
        _logger.LogInformation("Splitted Total {count} events, SplitIndex {splitIndex}", _buffer.Count, splitIndex);
        
        // Process old events
        if (splitIndex >= 0)
        {
            _logger.LogInformation("Working on old events");
            var sortableUniqueIdFrom = _safeState?.GetLastSortableUniqueId() ?? SortableUniqueIdValue.MinValue;
            
            // Get old events
            var oldEvents = _buffer.Take(splitIndex + 1)
                .Where(e => new SortableUniqueIdValue(e.SortableUniqueId).IsLaterThan(sortableUniqueIdFrom))
                .ToList();
            
            if (oldEvents.Any())
            {
                // Apply to safe state
                var newSafeState = _domainTypes.MultiProjectorsType.Project(
                    _safeState?.ProjectorCommon ?? projector, oldEvents);
                
                if (newSafeState.IsSuccess)
                {
                    var lastOldEvt = oldEvents.Last();
                    _safeState = new MultiProjectionState(
                        newSafeState.GetValue(),
                        lastOldEvt.Id,
                        lastOldEvt.SortableUniqueId,
                        (_safeState?.Version ?? 0) + 1,
                        0,
                        _safeState?.RootPartitionKey ?? "default");
                    
                    // Mark for saving
                    _pendingSave = true;
                }
            }
            
            // Remove processed events from buffer
            _buffer.RemoveRange(0, splitIndex + 1);
        }
        
        _logger.LogInformation("After worked old events Total {count} events", _buffer.Count);
        
        // Process remaining (newer) events for unsafe state
        if (_buffer.Any() && _safeState != null)
        {
            var newUnsafeState = _domainTypes.MultiProjectorsType.Project(_safeState.ProjectorCommon, _buffer);
            
            if (newUnsafeState.IsSuccess)
            {
                var lastNewEvt = _buffer.Last();
                _unsafeState = new MultiProjectionState(
                    newUnsafeState.GetValue(),
                    lastNewEvt.Id,
                    lastNewEvt.SortableUniqueId,
                    _safeState.Version + 1,
                    0,
                    _safeState.RootPartitionKey);
            }
        }
        else
        {
            _unsafeState = null;
        }
        
        _logger.LogInformation("Finish flush buffer {count} events", _buffer.Count);
        LogState();
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
            "default");
    }

    private async Task PersistStateAsync(MultiProjectionState state)
    {
        if (state.Version == 0) return;
        
        _logger.LogInformation("Persisting state {version}", state.Version);
        
        var serializableState = await SerializableMultiProjectionState.CreateFromAsync(state, _domainTypes);
        await StateManager.SetStateAsync(StateKey, serializableState);
        
        _logger.LogInformation("Persisting state written {version}", state.Version);
    }

    #endregion

    #region Public API (IMultiProjectorActor)

    public async Task<SerializableQueryResult> QueryAsync(SerializableQuery query)
    {
        try
        {
            await EnsureStateLoadedAsync();
            
            // Deserialize the query
            var queryResult = await query.ToQueryAsync(_domainTypes);
            if (!queryResult.IsSuccess)
            {
                var errorResult = await SerializableQueryResult.CreateFromResultBoxAsync(
                    ResultBox<object>.FromException(queryResult.GetException()),
                    null!, // Null for failed deserialization
                    _domainTypes.JsonSerializerOptions);
                return errorResult.GetValue();
            }
            
            var queryCommon = queryResult.GetValue();
            
            var res = await _domainTypes.QueryTypes.ExecuteAsQueryResult(
                queryCommon, 
                GetProjectorForQuery, 
                new ServiceCollection().BuildServiceProvider());
            
            if (res == null)
            {
                var errorResult = await SerializableQueryResult.CreateFromResultBoxAsync(
                    ResultBox<object>.FromException(new ApplicationException("Query not found")),
                    queryCommon,
                    _domainTypes.JsonSerializerOptions);
                return errorResult.GetValue();
            }
            
            var resultBox = res.Remap(v => v.ToGeneral(queryCommon));
            var serializableResult = await SerializableQueryResult.CreateFromResultBoxAsync(
                resultBox,
                queryCommon,
                _domainTypes.JsonSerializerOptions);
            return serializableResult.GetValue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing query");
            var errorResult = await SerializableQueryResult.CreateFromResultBoxAsync(
                ResultBox<object>.FromException(ex),
                null!, // Null for error case
                _domainTypes.JsonSerializerOptions);
            return errorResult.GetValue();
        }
    }

    public async Task<SerializableListQueryResult> QueryListAsync(SerializableListQuery query)
    {
        try
        {
            await EnsureStateLoadedAsync();
            
            // Deserialize the query
            var queryResult = await query.ToListQueryAsync(_domainTypes);
            if (!queryResult.IsSuccess)
            {
                var errorResult = await SerializableListQueryResult.CreateFromResultBoxAsync(
                    ResultBox<IListQueryResult>.FromException(queryResult.GetException()),
                    null!, // Null for failed deserialization
                    _domainTypes.JsonSerializerOptions);
                return errorResult.GetValue();
            }
            
            var queryCommon = queryResult.GetValue();
            
            var res = await _domainTypes.QueryTypes.ExecuteAsQueryResult(
                queryCommon, 
                GetProjectorForQuery, 
                new ServiceCollection().BuildServiceProvider());
            
            if (res == null)
            {
                var errorResult = await SerializableListQueryResult.CreateFromResultBoxAsync(
                    ResultBox<IListQueryResult>.FromException(new ApplicationException("Query not found")),
                    queryCommon,
                    _domainTypes.JsonSerializerOptions);
                return errorResult.GetValue();
            }
            
            var serializableResult = await SerializableListQueryResult.CreateFromResultBoxAsync(
                res,
                queryCommon,
                _domainTypes.JsonSerializerOptions);
            return serializableResult.GetValue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing list query");
            var errorResult = await SerializableListQueryResult.CreateFromResultBoxAsync(
                ResultBox<IListQueryResult>.FromException(ex),
                null!, // Null for error case
                _domainTypes.JsonSerializerOptions);
            return errorResult.GetValue();
        }
    }

    public async Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId)
    {
        await EnsureStateLoadedAsync();
        
        // Check if in buffer
        if (_buffer.Any(e => new SortableUniqueIdValue(e.SortableUniqueId)
            .IsLaterThanOrEqual(new SortableUniqueIdValue(sortableUniqueId))))
        {
            return true;
        }
        
        // Check if already processed
        if (!string.IsNullOrEmpty(_safeState?.LastSortableUniqueId))
        {
            var lastId = new SortableUniqueIdValue(_safeState.LastSortableUniqueId);
            var targetId = new SortableUniqueIdValue(sortableUniqueId);
            
            if (lastId.IsLaterThanOrEqual(targetId))
            {
                return true;
            }
        }
        
        return false;
    }

    public async Task BuildStateAsync()
    {
        await EnsureStateLoadedAsync();
        FlushBuffer();
    }

    public async Task RebuildStateAsync()
    {
        _safeState = null;
        _unsafeState = null;
        _buffer.Clear();
        await CatchUpFromStoreAsync();
        _pendingSave = true;
    }

    #endregion

    #region Event Handling via PubSub

    /// <summary>
    /// Handles events published through Dapr PubSub.
    /// This is called by the EventPubSubController when events are received.
    /// </summary>
    public async Task HandlePublishedEvent(DaprEventEnvelope envelope)
    {
        try
        {
            _logger.LogInformation("Received event from PubSub: EventId={EventId}, AggregateId={AggregateId}, Version={Version}", 
                envelope.EventId, envelope.AggregateId, envelope.Version);

            // Ensure state is loaded before processing
            await EnsureStateLoadedAsync();

            // Deserialize the event
            var @event = await _serialization.DeserializeEventAsync(envelope);
            if (@event == null)
            {
                _logger.LogWarning("Failed to deserialize event from envelope");
                return;
            }
            
            // Check if we've already processed this event
            if (await IsSortableUniqueIdReceived(@event.SortableUniqueId))
            {
                _logger.LogDebug("Event already processed: {SortableUniqueId}", @event.SortableUniqueId);
                return;
            }
            
            // Add to buffer if not duplicate
            if (!_buffer.Any(e => e.SortableUniqueId == @event.SortableUniqueId))
            {
                _buffer.Add(@event);
                _logger.LogDebug("Added event to buffer: {SortableUniqueId}", @event.SortableUniqueId);
                
                // Flush buffer immediately for real-time updates
                if (!_bootstrapping)
                {
                    FlushBuffer();
                    _pendingSave = true; // Mark state as pending save
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling published event");
        }
    }

    #endregion

    #region Helpers

    private IMultiProjectorCommon GetProjectorFromName()
    {
        var projectorName = Id.GetId();
        return _domainTypes.MultiProjectorsType.GetProjectorFromMultiProjectorName(projectorName);
    }

    private async Task<ResultBox<IMultiProjectorStateCommon>> GetProjectorForQuery(IMultiProjectionEventSelector _)
    {
        await EnsureStateLoadedAsync();
        FlushBuffer();
        
        var state = _unsafeState ?? _safeState;
        if (state == null)
        {
            return ResultBox<IMultiProjectorStateCommon>.FromException(
                new InvalidOperationException("No state available"));
        }
        
        return ResultBox<IMultiProjectorStateCommon>.FromValue(state);
    }

    #endregion
}