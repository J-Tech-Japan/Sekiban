using Dapr.Actors;
using Dapr.Actors.Runtime;
using Dapr.Actors.Client;
using Google.Protobuf;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Dapr.Parts;
using Sekiban.Pure.Dapr.Protos;
using Sekiban.Pure.Dapr.Serialization;
using Microsoft.Extensions.Logging;

namespace Sekiban.Pure.Dapr.Actors;

/// <summary>
/// Protobuf-enabled Dapr actor for aggregate projection and command execution.
/// This actor uses Protobuf for all communication to ensure efficient serialization.
/// </summary>
[Actor(TypeName = nameof(ProtobufAggregateActor))]
public class ProtobufAggregateActor : Actor, IProtobufAggregateActor
{
    private readonly SekibanDomainTypes _sekibanDomainTypes;
    private readonly IServiceProvider _serviceProvider;
    private readonly IActorProxyFactory _actorProxyFactory;
    private readonly IDaprProtobufSerializationService _serialization;
    private readonly ILogger<ProtobufAggregateActor> _logger;
    
    private const string StateKey = "aggregateState";
    private const string PartitionInfoKey = "partitionInfo";
    
    private Aggregate _currentAggregate = Aggregate.Empty;
    private PartitionKeysAndProjector? _partitionInfo;
    private bool _hasUnsavedChanges = false;
    
    public ProtobufAggregateActor(
        ActorHost host,
        SekibanDomainTypes sekibanDomainTypes,
        IServiceProvider serviceProvider,
        IActorProxyFactory actorProxyFactory,
        IDaprProtobufSerializationService serialization,
        ILogger<ProtobufAggregateActor> logger) : base(host)
    {
        _sekibanDomainTypes = sekibanDomainTypes ?? throw new ArgumentNullException(nameof(sekibanDomainTypes));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _actorProxyFactory = actorProxyFactory ?? throw new ArgumentNullException(nameof(actorProxyFactory));
        _serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
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

    public async Task<byte[]> GetStateAsync()
    {
        var eventHandlerActor = GetEventHandlerActor();
        var aggregate = await LoadStateInternalAsync(eventHandlerActor);
        
        // Convert to protobuf and return as byte array
        var protobufEnvelope = await _serialization.SerializeAggregateToProtobufAsync(aggregate);
        return protobufEnvelope.ToByteArray();
    }

    public async Task<byte[]> ExecuteCommandAsync(byte[] commandData)
    {
        try
        {
            // Deserialize the protobuf command request
            var request = ExecuteCommandRequest.Parser.ParseFrom(commandData);
            
            // Deserialize the command from the envelope
            var command = await _serialization.DeserializeCommandFromProtobufAsync(request.Command);
            if (command == null)
            {
                throw new InvalidOperationException("Failed to deserialize command");
            }
            
            // Create metadata
            var metadata = new CommandMetadata
            {
                AggregateId = _partitionInfo?.PartitionKeys.AggregateId ?? Guid.Empty,
                RootPartitionKey = _partitionInfo?.PartitionKeys.RootPartitionKey ?? string.Empty,
                ExecutedAt = DateTime.UtcNow,
                ExecutedBy = "dapr-actor"
            };
            
            // Execute command using existing logic
            var response = await ExecuteCommandInternalAsync(command, metadata);
            
            // Convert response to protobuf
            var protobufResponse = new ProtobufCommandResponse
            {
                Success = response.IsSuccess,
                ErrorMessage = response.ErrorMessage ?? string.Empty
            };
            
            if (response.IsSuccess && response.Aggregate != null)
            {
                var aggregateEnvelope = await _serialization.SerializeAggregateToProtobufAsync(response.Aggregate);
                protobufResponse.Aggregate = aggregateEnvelope;
            }
            
            foreach (var @event in response.Events)
            {
                var eventEnvelope = await _serialization.SerializeEventToProtobufAsync(
                    @event,
                    _partitionInfo?.PartitionKeys.AggregateId ?? Guid.Empty,
                    response.Version,
                    _partitionInfo?.PartitionKeys.RootPartitionKey ?? string.Empty);
                protobufResponse.Events.Add(eventEnvelope);
            }
            
            if (response.Metadata != null)
            {
                foreach (var kvp in response.Metadata)
                {
                    protobufResponse.Metadata[kvp.Key] = kvp.Value;
                }
            }
            
            return protobufResponse.ToByteArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command");
            
            // Return error response
            var errorResponse = new ProtobufCommandResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
            return errorResponse.ToByteArray();
        }
    }

    public async Task<byte[]> RebuildStateAsync()
    {
        var aggregate = await RebuildStateInternalAsync();
        var protobufEnvelope = await _serialization.SerializeAggregateToProtobufAsync(aggregate);
        return protobufEnvelope.ToByteArray();
    }

    private async Task<CommandResponse> ExecuteCommandInternalAsync(
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

    private async Task<Aggregate> RebuildStateInternalAsync()
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
        
        // Try to get saved state - now using protobuf format
        var savedStateBytes = await StateManager.TryGetStateAsync<byte[]>(StateKey);
        
        if (savedStateBytes.HasValue && savedStateBytes.Value != null)
        {
            try
            {
                var protobufEnvelope = ProtobufAggregateEnvelope.Parser.ParseFrom(savedStateBytes.Value);
                var aggregate = await _serialization.DeserializeAggregateFromProtobufAsync(protobufEnvelope);
                
                if (aggregate != null)
                {
                    // Check if projector version matches
                    if (_partitionInfo.Projector.GetVersion() != aggregate.ProjectorVersion)
                    {
                        // Projector version changed, rebuild
                        _hasUnsavedChanges = true;
                        return await RebuildStateInternalAsync();
                    }
                    
                    // Check if we have newer events
                    var lastEventId = await eventHandlerActor.GetLastSortableUniqueIdAsync();
                    if (lastEventId != aggregate.LastSortableUniqueId)
                    {
                        // Get delta events and project them
                        var deltaEvents = await eventHandlerActor.GetDeltaEventsAsync(
                            aggregate.LastSortableUniqueId, -1);
                        
                        // Create a new aggregate by projecting the delta events
                        var concreteAggregate = aggregate as Aggregate ?? throw new InvalidOperationException("Aggregate must be of type Aggregate");
                        var projectedResult = concreteAggregate.Project(deltaEvents.ToList(), _partitionInfo.Projector);
                        if (!projectedResult.IsSuccess)
                        {
                            throw new InvalidOperationException($"Failed to project delta events: {projectedResult.GetException().Message}");
                        }
                        _currentAggregate = projectedResult.GetValue();
                        _hasUnsavedChanges = true;
                    }
                    
                    return aggregate as Aggregate ?? throw new InvalidOperationException("Aggregate must be of type Aggregate");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize saved state, will rebuild from events");
            }
        }
        
        // No valid state found, rebuild from events
        _hasUnsavedChanges = true;
        return await RebuildStateInternalAsync();
    }

    private new async Task SaveStateAsync()
    {
        var protobufEnvelope = await _serialization.SerializeAggregateToProtobufAsync(_currentAggregate);
        await StateManager.SetStateAsync(StateKey, protobufEnvelope.ToByteArray());
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