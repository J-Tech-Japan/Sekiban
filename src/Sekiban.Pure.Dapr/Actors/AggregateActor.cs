using Dapr.Actors;
using Dapr.Actors.Client;
using Dapr.Actors.Runtime;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Dapr.Parts;
using Sekiban.Pure.Dapr.Serialization;
using Sekiban.Pure.Events;
using System.Text;
using System.Text.Json;

namespace Sekiban.Pure.Dapr.Actors;

/// <summary>
///     Dapr actor for aggregate projection and command execution.
///     This is the Dapr equivalent of Orleans' AggregateProjectorGrain.
/// </summary>
[Actor(TypeName = nameof(AggregateActor))]
public class AggregateActor : Actor, IAggregateActor, IRemindable
{

    private const string StateKey = "aggregateState";
    private const string PartitionInfoKey = "partitionInfo";
    private readonly IActorProxyFactory _actorProxyFactory;
    private readonly ILogger<AggregateActor> _logger;
    private readonly SekibanDomainTypes _sekibanDomainTypes;
    private readonly IDaprSerializationService _serialization;
    private readonly IServiceProvider _serviceProvider;

    private Aggregate _currentAggregate = Aggregate.Empty;
    private bool _hasUnsavedChanges;
    private PartitionKeysAndProjector? _partitionInfo;

    public AggregateActor(
        ActorHost host,
        SekibanDomainTypes sekibanDomainTypes,
        IServiceProvider serviceProvider,
        IActorProxyFactory actorProxyFactory,
        IDaprSerializationService serialization,
        ILogger<AggregateActor> logger) : base(host)
    {
        _sekibanDomainTypes = sekibanDomainTypes ?? throw new ArgumentNullException(nameof(sekibanDomainTypes));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _actorProxyFactory = actorProxyFactory ?? throw new ArgumentNullException(nameof(actorProxyFactory));
        _serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // Interface implementation - returns SerializableAggregate
    public async Task<SerializableAggregate> GetAggregateStateAsync()
    {
        var aggregate = await GetStateInternalAsync();
        return await SerializableAggregate.CreateFromAsync(aggregate, _sekibanDomainTypes.JsonSerializerOptions);
    }

    // New envelope-based RebuildStateAsync
    async Task<SerializableAggregate> IAggregateActor.RebuildStateAsync()
    {
        var aggregate = await RebuildStateAsync();
        return await SerializableAggregate.CreateFromAsync(aggregate, _sekibanDomainTypes.JsonSerializerOptions);
    }

    // New SerializableCommandAndMetadata-based ExecuteCommandAsync
    public async Task<SerializedCommandResponse> ExecuteCommandAsync(SerializableCommandAndMetadata commandAndMetadata)
    {
        try
        {
            // Convert back to command and metadata
            var result = await commandAndMetadata.ToCommandAndMetadataAsync(_sekibanDomainTypes);
            if (!result.HasValue)
            {
                return new SerializableCommandResponse
                {
                    AggregateId = Guid.Empty,
                    Group = PartitionKeys.DefaultAggregateGroupName,
                    RootPartitionKey = PartitionKeys.DefaultRootPartitionKey,
                    Version = 0,
                    Events = new List<SerializableCommandResponse.SerializableEvent>()
                };
            }

            var (command, metadata) = result.Value;

            // Execute command using legacy method
            var response = await ExecuteCommandAsync(command, metadata);

            return await SerializableCommandResponse.CreateFromAsync(response, _sekibanDomainTypes.JsonSerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute command from envelope");
            return new SerializableCommandResponse
            {
                AggregateId = Guid.Empty,
                Group = PartitionKeys.DefaultAggregateGroupName,
                RootPartitionKey = PartitionKeys.DefaultRootPartitionKey,
                Version = 0,
                Events = new List<SerializableCommandResponse.SerializableEvent>()
            };
        }
    }

    public Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
    {
        _logger.LogInformation("Received reminder: {ReminderName}", reminderName);
        return Task.CompletedTask;
    }

    protected override async Task OnActivateAsync()
    {
        await base.OnActivateAsync();
        _logger.LogInformation("AggregateActor {ActorId} activated", Id.GetId());

        // Register timer for periodic state saving
        // Note: Initialization is now deferred to first command execution
        await RegisterTimerAsync(
            "SaveState",
            nameof(SaveStateCallbackAsync),
            null,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(10));
    }

    protected override async Task OnDeactivateAsync()
    {
        if (_hasUnsavedChanges)
        {
            await SaveStateAsync();
        }

        await base.OnDeactivateAsync();
    }

    // Legacy method - kept for compatibility
    public async Task<Aggregate> GetStateInternalAsync()
    {
        // Ensure initialization on first use
        await EnsureInitializedAsync();

        var eventHandlerActor = GetEventHandlerActor();
        return await LoadStateInternalAsync(eventHandlerActor);
    }

    // Legacy method - kept for compatibility
    private async Task<SekibanCommandResponse> ExecuteCommandAsync(
        ICommandWithHandlerSerializable command,
        CommandMetadata metadata)
    {
        // Ensure initialization on first use (deferred from OnActivateAsync)
        await EnsureInitializedAsync();

        if (_partitionInfo == null)
        {
            throw new InvalidOperationException("Partition info not initialized");
        }

        var eventHandlerActor = GetEventHandlerActor();

        // Ensure we have the latest state
        if (_currentAggregate == null || _currentAggregate == Aggregate.Empty)
        {
            _currentAggregate = await LoadStateInternalAsync(eventHandlerActor);
        }

        // Create repository for this actor
        var repository = new DaprRepository(
            eventHandlerActor,
            _partitionInfo.PartitionKeys,
            _partitionInfo.Projector,
            _sekibanDomainTypes.EventTypes,
            _currentAggregate,
            _sekibanDomainTypes);

        // Execute command
        var commandExecutor = new CommandExecutor(_serviceProvider)
        {
            EventTypes = _sekibanDomainTypes.EventTypes
        };

        var result = await _sekibanDomainTypes
            .CommandTypes
            .ExecuteGeneral(
                commandExecutor,
                command,
                _partitionInfo.PartitionKeys,
                metadata,
                (_, _) => Task.FromResult(repository.GetAggregate()),
                repository.Save)
            .UnwrapBox();

        // Debug logging
        _logger.LogDebug("Command execution completed. Events produced: {EventCount}, Version before: {VersionBefore}", 
            result.Events.Count, _currentAggregate.Version);
        
        // Update current aggregate with new events
        _currentAggregate = repository.GetProjectedAggregate(result.Events).UnwrapBox();
        
        _logger.LogDebug("After projection. Version after: {VersionAfter}", _currentAggregate.Version);
        
        // Only mark as changed if events were actually produced
        if (result.Events.Count > 0)
        {
            _hasUnsavedChanges = true;
            _logger.LogDebug("Marked aggregate as having unsaved changes");
        }
        else
        {
            _logger.LogDebug("No events produced, not marking as changed");
        }

        return result;
    }

    // Legacy method - kept for compatibility
    public async Task<Aggregate> RebuildStateAsync()
    {
        // Ensure initialization on first use
        await EnsureInitializedAsync();

        if (_partitionInfo == null)
        {
            throw new InvalidOperationException("Partition info not initialized");
        }

        var eventHandlerActor = GetEventHandlerActor();

        // Create repository for rebuilding
        var repository = new DaprRepository(
            eventHandlerActor,
            _partitionInfo.PartitionKeys,
            _partitionInfo.Projector,
            _sekibanDomainTypes.EventTypes,
            Aggregate.EmptyFromPartitionKeys(_partitionInfo.PartitionKeys),
            _sekibanDomainTypes);

        // Load all events and rebuild state
        var aggregate = await repository.Load().UnwrapBox();
        _currentAggregate = aggregate;

        // Save the rebuilt state
        await SaveStateAsync();

        return aggregate;
    }

    private async Task<PartitionKeysAndProjector> GetPartitionInfoAsync()
    {
        // Try to get saved partition info
        var savedInfo = await StateManager.TryGetStateAsync<SerializedPartitionInfo>(PartitionInfoKey);
        
        if (savedInfo.HasValue && !string.IsNullOrEmpty(savedInfo.Value.GrainKey))
        {
            return PartitionKeysAndProjector
                .FromGrainKey(savedInfo.Value.GrainKey, _sekibanDomainTypes.AggregateProjectorSpecifier)
                .UnwrapBox();
        }

        // Parse from actor ID
        var actorId = Id.GetId();
        var grainKey = actorId.Contains(':') ? actorId.Substring(actorId.IndexOf(':') + 1) : actorId;

        var partitionInfo = PartitionKeysAndProjector
            .FromGrainKey(grainKey, _sekibanDomainTypes.AggregateProjectorSpecifier)
            .UnwrapBox();

        // Save for future use
        await StateManager.SetStateAsync(PartitionInfoKey, new SerializedPartitionInfo { GrainKey = grainKey });

        return partitionInfo;
    }

    private IAggregateEventHandlerActor GetEventHandlerActor()
    {
        if (_partitionInfo == null)
        {
            throw new InvalidOperationException("Partition info not initialized. Call EnsureInitializedAsync() first.");
        }

        var eventHandlerKey = _partitionInfo.ToEventHandlerGrainKey();
        var eventHandlerActorId = new ActorId($"eventhandler:{eventHandlerKey}");

        return _actorProxyFactory.CreateActorProxy<IAggregateEventHandlerActor>(
            eventHandlerActorId,
            nameof(AggregateEventHandlerActor));
    }

    private async Task<Aggregate> LoadStateInternalAsync(IAggregateEventHandlerActor eventHandlerActor)
    {
        if (_partitionInfo == null)
        {
            throw new InvalidOperationException("Partition info not initialized");
        }

        // Try to get saved state
        var savedState = await StateManager.TryGetStateAsync<DaprAggregateSurrogate>(StateKey);

        if (savedState.HasValue)
        {
            var aggregate = await _serialization.DeserializeAggregateAsync(savedState.Value);
            if (aggregate != null)
            {

                // Check if projector version matches
                if (_partitionInfo.Projector.GetVersion() != aggregate.ProjectorVersion)
                {
                    // Projector version changed, rebuild
                    _hasUnsavedChanges = true;
                    return await RebuildStateAsync();
                }

                // Get delta events and project them
                var deltaEventDocuments = await eventHandlerActor.GetDeltaEventsAsync(
                    aggregate.LastSortableUniqueId,
                    -1);

                // Convert documents back to events
                var deltaEvents = new List<IEvent>();
                foreach (var document in deltaEventDocuments)
                {
                    var eventResult = await document.ToEventAsync(_sekibanDomainTypes);
                    if (eventResult.HasValue)
                    {
                        deltaEvents.Add(eventResult.Value);
                    }
                }

                // Create a new aggregate by projecting the delta events
                var concreteAggregate = aggregate as Aggregate ??
                    throw new InvalidOperationException("Aggregate must be of type Aggregate");
                
                // Only project and mark as changed if there are delta events
                if (deltaEvents.Count > 0)
                {
                    var projectedResult = concreteAggregate.Project(deltaEvents, _partitionInfo.Projector);
                    if (!projectedResult.IsSuccess)
                    {
                        throw new InvalidOperationException(
                            $"Failed to project delta events: {projectedResult.GetException().Message}");
                    }
                    _currentAggregate = projectedResult.GetValue();
                    _hasUnsavedChanges = true;
                }
                else
                {
                    _currentAggregate = concreteAggregate;
                }

                return _currentAggregate;
            }
        }

        // No valid state found, rebuild from events
        _hasUnsavedChanges = true;
        return await RebuildStateAsync();
    }

    private new async Task SaveStateAsync()
    {
        var surrogate = await _serialization.SerializeAggregateAsync(_currentAggregate);
        await StateManager.SetStateAsync(StateKey, surrogate);
        _hasUnsavedChanges = false;
    }

    public async Task SaveStateCallbackAsync(object? state)
    {
        if (_hasUnsavedChanges)
        {
            await SaveStateAsync();
        }
    }

    /// <summary>
    ///     Ensures that the actor is properly initialized on first use.
    ///     This is called from ExecuteCommandAsync to defer initialization until actually needed.
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_partitionInfo != null && _currentAggregate != Aggregate.Empty)
        {
            return; // Already initialized
        }

        try
        {
            _logger.LogDebug("Initializing AggregateActor {ActorId} on first use", Id.GetId());

            // Initialize partition info
            if (_partitionInfo == null)
            {
                _partitionInfo = await GetPartitionInfoAsync();
            }

            // Get event handler actor and load initial state
            if (_currentAggregate == Aggregate.Empty)
            {
                var eventHandlerActor = GetEventHandlerActor();
                _currentAggregate = await LoadStateInternalAsync(eventHandlerActor);
            }

            _logger.LogDebug("AggregateActor {ActorId} initialization completed", Id.GetId());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during actor initialization");
            throw;
        }
    }


    /// <summary>
    ///     Serializable partition info for state storage
    /// </summary>
    private record SerializedPartitionInfo
    {
        public string GrainKey { get; init; } = string.Empty;
    }
}
