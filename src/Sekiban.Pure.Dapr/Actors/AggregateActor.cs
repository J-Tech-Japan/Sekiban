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
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
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
    public async Task<SerializableCommandResponse> ExecuteCommandAsync(
        SerializableCommandAndMetadata commandAndMetadata)
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

            // Use Dapr serialization options instead of domain-specific options for framework types
            return await SerializableCommandResponse.CreateFromAsync(
                response,
                DaprSerializationOptions.Default.JsonSerializerOptions);
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

    public Task ReceiveReminderAsync(string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period) =>
        Task.CompletedTask;

    protected override async Task OnActivateAsync()
    {
        await base.OnActivateAsync();

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
    private async Task<CommandResponse> ExecuteCommandAsync(
        ICommandWithHandlerSerializable command,
        CommandMetadata metadata)
    {
        await EnsureInitializedAsync();

        if (_partitionInfo == null)
        {
            throw new InvalidOperationException("Partition info not initialized");
        }

        var eventHandlerActor = GetEventHandlerActor();

        if (_currentAggregate == Aggregate.Empty)
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

        // Execute command with retry logic for concurrency conflicts
        const int maxRetries = 3;
        CommandResponse? result = null;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var commandExecutor = new CommandExecutor(_serviceProvider)
                {
                    EventTypes = _sekibanDomainTypes.EventTypes
                };

                var executeResult = await _sekibanDomainTypes.CommandTypes.ExecuteGeneral(
                    commandExecutor,
                    command,
                    _partitionInfo.PartitionKeys,
                    metadata,
                    (_, _) => Task.FromResult(repository.GetAggregate()),
                    repository.Save);

                if (!executeResult.IsSuccess)
                {
                    var ex = executeResult.GetException();
                    if (ex.Message.Contains("Expected last event ID") && attempt < maxRetries - 1)
                    {
                        // Concurrency conflict - retry with exponential backoff
                        _logger.LogInformation(
                            "Concurrency conflict detected, retrying attempt {Attempt}/{MaxRetries}",
                            attempt + 1,
                            maxRetries);
                        await Task.Delay(50 * (attempt + 1));

                        // Reload state and update repository
                        _currentAggregate = await LoadStateInternalAsync(eventHandlerActor);
                        repository = new DaprRepository(
                            eventHandlerActor,
                            _partitionInfo.PartitionKeys,
                            _partitionInfo.Projector,
                            _sekibanDomainTypes.EventTypes,
                            _currentAggregate,
                            _sekibanDomainTypes);
                        continue;
                    }
                    throw ex;
                }

                result = executeResult.GetValue();
                break;
            }
            catch (InvalidCastException ex) when (ex.Message.Contains("Expected last event ID") &&
                attempt < maxRetries - 1)
            {
                // Handle exceptions thrown by UnwrapBox
                _logger.LogInformation(
                    "Concurrency conflict detected (exception), retrying attempt {Attempt}/{MaxRetries}",
                    attempt + 1,
                    maxRetries);
                await Task.Delay(50 * (attempt + 1));

                // Reload state and update repository
                _currentAggregate = await LoadStateInternalAsync(eventHandlerActor);
                repository = new DaprRepository(
                    eventHandlerActor,
                    _partitionInfo.PartitionKeys,
                    _partitionInfo.Projector,
                    _sekibanDomainTypes.EventTypes,
                    _currentAggregate,
                    _sekibanDomainTypes);
            }
        }

        if (result == null)
        {
            throw new InvalidOperationException(
                "Failed to execute command after maximum retries due to concurrency conflicts");
        }

        // Update current aggregate with new events
        _currentAggregate = repository.GetProjectedAggregate(result.Events).UnwrapBox();

        // Only mark as changed if events were actually produced
        if (result.Events.Count > 0)
        {
            _hasUnsavedChanges = true;
        }

        return result;
    }

    /// <summary>
    ///     Rebuilds the aggregate state from all events without calling LoadStateInternalAsync to prevent infinite loops
    /// </summary>
    public async Task<Aggregate> RebuildStateAsync()
    {
        // Ensure initialization on first use
        await EnsureInitializedAsync();

        if (_partitionInfo == null)
        {
            throw new InvalidOperationException("Partition info not initialized");
        }

        var eventHandlerActor = GetEventHandlerActor();

        // Create repository for rebuilding with empty aggregate
        var emptyAggregate = Aggregate.EmptyFromPartitionKeys(_partitionInfo.PartitionKeys);
        var repository = new DaprRepository(
            eventHandlerActor,
            _partitionInfo.PartitionKeys,
            _partitionInfo.Projector,
            _sekibanDomainTypes.EventTypes,
            emptyAggregate,
            _sekibanDomainTypes);

        // Load all events and rebuild state directly from repository
        var aggregate = await repository.Load().UnwrapBox();
        _currentAggregate = aggregate;
        _hasUnsavedChanges = true;

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

        var savedState = await StateManager.TryGetStateAsync<DaprAggregateSurrogate>(StateKey);

        if (savedState.HasValue)
        {
            try
            {
                var aggregate = await _serialization.DeserializeAggregateAsync(savedState.Value);
                if (aggregate == null)
                {
                    _logger.LogWarning("Failed to deserialize aggregate state, will rebuild from events");
                    // Continue to rebuild from events below
                } else
                {
                    if (_partitionInfo.Projector.GetVersion() != aggregate.ProjectorVersion)
                    {
                        var emptyAggregate = Aggregate.EmptyFromPartitionKeys(_partitionInfo.PartitionKeys);
                        var repository = new DaprRepository(
                            eventHandlerActor,
                            _partitionInfo.PartitionKeys,
                            _partitionInfo.Projector,
                            _sekibanDomainTypes.EventTypes,
                            emptyAggregate,
                            _sekibanDomainTypes);

                        var rebuiltAggregate = await repository.Load().UnwrapBox();
                        _currentAggregate = rebuiltAggregate;
                        _hasUnsavedChanges = true;
                        return rebuiltAggregate;
                    }

                    var deltaEventDocuments = await eventHandlerActor.GetDeltaEventsAsync(
                        aggregate.LastSortableUniqueId,
                        -1);

                    var deltaEvents = new List<IEvent>();
                    foreach (var document in deltaEventDocuments)
                    {
                        var eventResult = await document.ToEventAsync(_sekibanDomainTypes);
                        if (eventResult.HasValue && eventResult.Value != null)
                        {
                            deltaEvents.Add(eventResult.Value);
                        }
                    }

                    var concreteAggregate = aggregate as Aggregate ??
                        throw new InvalidOperationException("Aggregate must be of type Aggregate");

                    if (deltaEvents.Count > 0)
                    {
                        var projectedResult = concreteAggregate.Project(deltaEvents, _partitionInfo.Projector);
                        if (!projectedResult.IsSuccess)
                        {
                            _logger.LogWarning(
                                "Failed to project delta events: {Error}. Rebuilding from scratch.",
                                projectedResult.GetException().Message);
                            // Fallback to rebuilding from scratch
                            return await RebuildStateAsync();
                        }
                        _currentAggregate = projectedResult.GetValue();
                        _hasUnsavedChanges = true;
                    } else
                    {
                        _currentAggregate = concreteAggregate;
                    }

                    return _currentAggregate;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception while deserializing aggregate state, will rebuild from events");
                // Fall through to rebuild from events
            }
        }

        var emptyAggregateForNewLoad = Aggregate.EmptyFromPartitionKeys(_partitionInfo.PartitionKeys);
        var repositoryForNewLoad = new DaprRepository(
            eventHandlerActor,
            _partitionInfo.PartitionKeys,
            _partitionInfo.Projector,
            _sekibanDomainTypes.EventTypes,
            emptyAggregateForNewLoad,
            _sekibanDomainTypes);

        var loadResult = await repositoryForNewLoad.Load();
        if (!loadResult.IsSuccess)
        {
            _logger.LogWarning("Failed to load aggregate from repository: {Error}", loadResult.GetException().Message);
            // Return empty aggregate as fallback
            _currentAggregate = emptyAggregateForNewLoad;
            _hasUnsavedChanges = false;
            return _currentAggregate;
        }

        var newAggregate = loadResult.GetValue();
        _currentAggregate = newAggregate;
        _hasUnsavedChanges = true;
        return newAggregate;
    }

    private async new Task SaveStateAsync()
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
        if (_partitionInfo != null)
        {
            return; // Already initialized
        }

        try
        {
            // Initialize partition info only
            _partitionInfo = await GetPartitionInfoAsync();
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
