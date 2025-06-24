using Dapr.Actors;
using Dapr.Actors.Runtime;
using Dapr.Actors.Client;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Dapr.Parts;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sekiban.Pure.Dapr.Actors;

/// <summary>
/// Dapr actor for aggregate projection and command execution.
/// This is the Dapr equivalent of Orleans' AggregateProjectorGrain.
/// </summary>
[Actor(TypeName = nameof(AggregateActor))]
public class AggregateActor : Actor, IAggregateActor
{
    private readonly SekibanDomainTypes _sekibanDomainTypes;
    private readonly IServiceProvider _serviceProvider;
    private readonly IActorProxyFactory _actorProxyFactory;
    private readonly ILogger<AggregateActor> _logger;
    
    private const string StateKey = "aggregateState";
    private const string PartitionInfoKey = "partitionInfo";
    
    private Aggregate _currentAggregate = Aggregate.Empty;
    private PartitionKeysAndProjector? _partitionInfo;
    private bool _hasUnsavedChanges = false;
    
    public AggregateActor(
        ActorHost host,
        SekibanDomainTypes sekibanDomainTypes,
        IServiceProvider serviceProvider,
        IActorProxyFactory actorProxyFactory,
        ILogger<AggregateActor> logger) : base(host)
    {
        _sekibanDomainTypes = sekibanDomainTypes ?? throw new ArgumentNullException(nameof(sekibanDomainTypes));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _actorProxyFactory = actorProxyFactory ?? throw new ArgumentNullException(nameof(actorProxyFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task OnActivateAsync()
    {
        await base.OnActivateAsync();
        
        try
        {
            // Initialize partition info
            _partitionInfo = await GetPartitionInfoAsync();
            
            // Get event handler actor
            var eventHandlerActor = GetEventHandlerActor();
            
            // Load initial state
            _currentAggregate = await LoadStateInternalAsync(eventHandlerActor);
            
            // Register timer for periodic state saving
            await RegisterTimerAsync(
                "SaveState",
                nameof(SaveStateCallbackAsync),
                null,
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during actor activation");
            throw;
        }
    }

    protected override async Task OnDeactivateAsync()
    {
        if (_hasUnsavedChanges)
        {
            await SaveStateAsync();
        }
        
        await base.OnDeactivateAsync();
    }

    public async Task<Aggregate> GetStateAsync()
    {
        var eventHandlerActor = GetEventHandlerActor();
        return await LoadStateInternalAsync(eventHandlerActor);
    }

    public async Task<CommandResponse> ExecuteCommandAsync(
        ICommandWithHandlerSerializable command,
        CommandMetadata metadata)
    {
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
            _currentAggregate);
        
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
        
        // Update current aggregate with new events
        _currentAggregate = repository.GetProjectedAggregate(result.Events).UnwrapBox();
        _hasUnsavedChanges = true;
        
        return result;
    }

    public async Task<Aggregate> RebuildStateAsync()
    {
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
            Aggregate.EmptyFromPartitionKeys(_partitionInfo.PartitionKeys));
        
        // Load all events and rebuild state
        var aggregate = await repository.Load().UnwrapBox();
        _currentAggregate = aggregate;
        
        // Save the rebuilt state
        await SaveStateAsync();
        
        return aggregate;
    }

    private async Task<PartitionKeysAndProjector> GetPartitionInfoAsync()
    {
        // Try to get from state first
        var storedInfo = await StateManager.TryGetStateAsync<SerializedPartitionInfo>(PartitionInfoKey);
        if (storedInfo.HasValue)
        {
            return PartitionKeysAndProjector.FromGrainKey(
                storedInfo.Value.GrainKey,
                _sekibanDomainTypes.AggregateProjectorSpecifier).UnwrapBox();
        }
        
        // Parse from actor ID
        var actorId = Id.GetId();
        var grainKey = actorId.Contains(':') 
            ? actorId.Substring(actorId.IndexOf(':') + 1) 
            : actorId;
        
        var partitionInfo = PartitionKeysAndProjector.FromGrainKey(
            grainKey,
            _sekibanDomainTypes.AggregateProjectorSpecifier).UnwrapBox();
        
        // Store for future use
        await StateManager.SetStateAsync(
            PartitionInfoKey, 
            new SerializedPartitionInfo { GrainKey = grainKey });
        
        return partitionInfo;
    }

    private IAggregateEventHandlerActor GetEventHandlerActor()
    {
        if (_partitionInfo == null)
        {
            throw new InvalidOperationException("Partition info not initialized");
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
        var savedState = await StateManager.TryGetStateAsync<DaprSerializableAggregate>(StateKey);
        
        if (savedState.HasValue)
        {
            var aggregateOptional = await savedState.Value.ToAggregateAsync(_sekibanDomainTypes);
            if (aggregateOptional.HasValue)
            {
                var aggregate = aggregateOptional.GetValue();
                
                // Check if projector version matches
                if (_partitionInfo.Projector.GetVersion() != aggregate.ProjectorVersion)
                {
                    // Projector version changed, rebuild
                    _hasUnsavedChanges = true;
                    return await RebuildStateAsync();
                }
                
                // Check if we have newer events
                var lastEventId = await eventHandlerActor.GetLastSortableUniqueIdAsync();
                if (lastEventId != aggregate.LastSortableUniqueId)
                {
                    // Get delta events and project them
                    var deltaEvents = await eventHandlerActor.GetDeltaEventsAsync(
                        aggregate.LastSortableUniqueId, -1);
                    
                    aggregate = aggregate.Project(deltaEvents.ToList(), _partitionInfo.Projector).UnwrapBox();
                    _currentAggregate = aggregate;
                    _hasUnsavedChanges = true;
                }
                
                return aggregate;
            }
        }
        
        // No valid state found, rebuild from events
        _hasUnsavedChanges = true;
        return await RebuildStateAsync();
    }

    private new async Task SaveStateAsync()
    {
        var serializableState = await DaprSerializableAggregate.CreateFromAsync(
            _currentAggregate,
            _sekibanDomainTypes.JsonSerializerOptions);
        
        await StateManager.SetStateAsync(StateKey, serializableState);
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
    /// Serializable partition info for state storage
    /// </summary>
    private record SerializedPartitionInfo
    {
        public string GrainKey { get; init; } = string.Empty;
    }
}