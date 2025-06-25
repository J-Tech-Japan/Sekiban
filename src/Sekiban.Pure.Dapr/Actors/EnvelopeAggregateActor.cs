using Dapr.Actors;
using Dapr.Actors.Runtime;
using Dapr.Actors.Client;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using SekibanCommandResponse = Sekiban.Pure.Command.Executor.CommandResponse;
using DaprCommandResponse = Sekiban.Pure.Dapr.Actors.CommandResponse;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Dapr.Parts;
using Sekiban.Pure.Dapr.Serialization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sekiban.Pure.Dapr.Actors;

/// <summary>
/// Dapr actor for aggregate projection and command execution using envelope-based communication.
/// This implementation uses concrete types (envelopes) for proper Dapr JSON serialization.
/// </summary>
[Actor(TypeName = nameof(EnvelopeAggregateActor))]
public class EnvelopeAggregateActor : Actor, IAggregateActor
{
    private readonly SekibanDomainTypes _sekibanDomainTypes;
    private readonly IServiceProvider _serviceProvider;
    private readonly IActorProxyFactory _actorProxyFactory;
    private readonly IEnvelopeProtobufService _envelopeService;
    private readonly IDaprSerializationService _serialization;
    private readonly ILogger<EnvelopeAggregateActor> _logger;
    
    private const string StateKey = "aggregateState";
    private const string PartitionInfoKey = "partitionInfo";
    
    private Aggregate _currentAggregate = Aggregate.Empty;
    private PartitionKeysAndProjector? _partitionInfo;
    private bool _hasUnsavedChanges = false;
    
    public EnvelopeAggregateActor(
        ActorHost host,
        SekibanDomainTypes sekibanDomainTypes,
        IServiceProvider serviceProvider,
        IActorProxyFactory actorProxyFactory,
        IEnvelopeProtobufService envelopeService,
        IDaprSerializationService serialization,
        ILogger<EnvelopeAggregateActor> logger) : base(host)
    {
        _sekibanDomainTypes = sekibanDomainTypes ?? throw new ArgumentNullException(nameof(sekibanDomainTypes));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _actorProxyFactory = actorProxyFactory ?? throw new ArgumentNullException(nameof(actorProxyFactory));
        _envelopeService = envelopeService ?? throw new ArgumentNullException(nameof(envelopeService));
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

    public async Task<string> GetStateAsync()
    {
        var eventHandlerActor = GetEventHandlerActor();
        var aggregate = await LoadStateInternalAsync(eventHandlerActor);
        
        // Serialize aggregate to JSON for return
        return JsonSerializer.Serialize(aggregate);
    }

    public async Task<DaprCommandResponse> ExecuteCommandAsync(CommandEnvelope envelope)
    {
        if (_partitionInfo == null)
        {
            throw new InvalidOperationException("Partition info not initialized");
        }

        try
        {
            _logger.LogDebug("Executing command {CommandType} for aggregate {AggregateId}",
                envelope.CommandType, envelope.AggregateId);

            // Extract command from envelope
            var command = await _envelopeService.ExtractCommand(envelope);
            
            // Create command metadata
            var metadata = new CommandMetadata(
                CommandId: Guid.NewGuid(),
                CausationId: envelope.Metadata.GetValueOrDefault("CausationId", string.Empty),
                CorrelationId: envelope.CorrelationId,
                ExecutedUser: envelope.Metadata.GetValueOrDefault("ExecutedUser", "system"));
            
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
            
            // Convert result to Protobuf-based response
            var protobufResponse = await _envelopeService.ConvertToProtobufResponse(result, _partitionInfo.PartitionKeys);
            
            // Send events to event handler actor using envelopes
            if (result.Events.Any())
            {
                var eventEnvelopes = await _envelopeService.CreateEventEnvelopes(
                    result.Events,
                    _partitionInfo.PartitionKeys,
                    envelope.CorrelationId);
                
                var lastEventId = _currentAggregate.LastSortableUniqueId;
                var handlingResponse = await eventHandlerActor.AppendEventsAsync(lastEventId, eventEnvelopes);
                
                if (!handlingResponse.IsSuccess)
                {
                    _logger.LogError("Failed to append events to event handler: {Error}", 
                        handlingResponse.ErrorMessage);
                }
            }
            
            return protobufResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing command {CommandType}", envelope.CommandType);
            
            var errorData = new
            {
                Message = ex.Message,
                Type = ex.GetType().Name,
                StackTrace = ex.StackTrace
            };
            
            return CommandResponse.Failure(JsonSerializer.Serialize(errorData));
        }
    }

    public async Task<string> RebuildStateAsync()
    {
        if (_partitionInfo == null)
        {
            throw new InvalidOperationException("Partition info not initialized");
        }
        
        var eventHandlerActor = GetEventHandlerActor();
        
        // Get all event envelopes
        var eventEnvelopes = await eventHandlerActor.GetAllEventsAsync();
        
        // Convert envelopes to events
        var events = await _envelopeService.ExtractEvents(eventEnvelopes);
        
        // Create repository for rebuilding
        var repository = new DaprRepository(
            eventHandlerActor,
            _partitionInfo.PartitionKeys,
            _partitionInfo.Projector,
            _sekibanDomainTypes.EventTypes,
            Aggregate.EmptyFromPartitionKeys(_partitionInfo.PartitionKeys));
        
        // Project all events to rebuild state
        var aggregate = Aggregate.EmptyFromPartitionKeys(_partitionInfo.PartitionKeys);
        foreach (var @event in events)
        {
            var projectedResult = aggregate.Project(new List<IEvent> { @event }, _partitionInfo.Projector);
            if (!projectedResult.IsSuccess)
            {
                throw new InvalidOperationException($"Failed to project event: {projectedResult.GetException().Message}");
            }
            aggregate = projectedResult.GetValue();
        }
        
        _currentAggregate = aggregate;
        
        // Save the rebuilt state
        await SaveStateAsync();
        
        // Return serialized state
        return JsonSerializer.Serialize(aggregate);
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
                    var rebuiltStateJson = await RebuildStateAsync();
                    return JsonSerializer.Deserialize<Aggregate>(rebuiltStateJson)!;
                }
                
                // Check if we have newer events
                var lastEventId = await eventHandlerActor.GetLastSortableUniqueIdAsync();
                if (lastEventId != aggregate.LastSortableUniqueId)
                {
                    // Get delta event envelopes and project them
                    var deltaEnvelopes = await eventHandlerActor.GetDeltaEventsAsync(
                        aggregate.LastSortableUniqueId, -1);
                    
                    var deltaEvents = await _envelopeService.ExtractEvents(deltaEnvelopes);
                    
                    // Create a new aggregate by projecting the delta events
                    var concreteAggregate = aggregate as Aggregate ?? throw new InvalidOperationException("Aggregate must be of type Aggregate");
                    var projectedResult = concreteAggregate.Project(deltaEvents, _partitionInfo.Projector);
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
        
        // No valid state found, rebuild from events
        _hasUnsavedChanges = true;
        var stateJson = await RebuildStateAsync();
        return JsonSerializer.Deserialize<Aggregate>(stateJson)!;
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
    /// Serializable partition info for state storage
    /// </summary>
    private record SerializedPartitionInfo
    {
        public string GrainKey { get; init; } = string.Empty;
    }
}