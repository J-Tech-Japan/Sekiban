using Dapr.Actors;
using Dapr.Actors.Client;
using Dapr.Client;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Dapr.Actors;
using Sekiban.Pure.Dapr.Configuration;
using Sekiban.Pure.Dapr.Serialization;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using SekibanCommandResponse = Sekiban.Pure.Command.Executor.CommandResponse;
using DaprCommandResponse = Sekiban.Pure.Dapr.Actors.CommandResponse;

namespace Sekiban.Pure.Dapr;

/// <summary>
/// Envelope-based Sekiban executor for Dapr that uses concrete types for actor communication
/// Accepts commands (both C# objects and Protobuf) and converts them to envelopes
/// </summary>
public class SekibanEnvelopeDaprExecutor : ISekibanExecutor
{
    private readonly DaprClient _daprClient;
    private readonly IActorProxyFactory _actorProxyFactory;
    private readonly SekibanDomainTypes _domainTypes;
    private readonly IEnvelopeProtobufService _envelopeService;
    private readonly DaprSekibanOptions _options;
    private readonly ILogger<SekibanEnvelopeDaprExecutor> _logger;

    public SekibanEnvelopeDaprExecutor(
        DaprClient daprClient,
        IActorProxyFactory actorProxyFactory,
        SekibanDomainTypes domainTypes,
        IEnvelopeProtobufService envelopeService,
        IOptions<DaprSekibanOptions> options,
        ILogger<SekibanEnvelopeDaprExecutor> logger)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _actorProxyFactory = actorProxyFactory ?? throw new ArgumentNullException(nameof(actorProxyFactory));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _envelopeService = envelopeService ?? throw new ArgumentNullException(nameof(envelopeService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ResultBox<SekibanCommandResponse>> CommandAsync(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null)
    {
        try
        {
            // Get projector type from command
            var projectorType = GetProjectorTypeFromCommand(command);
            if (projectorType == null)
            {
                return ResultBox<SekibanCommandResponse>.FromException(
                    new InvalidOperationException($"Could not determine projector type for command {command.GetType().Name}"));
            }

            // Get partition keys
            var partitionKeys = GetPartitionKeys(command);
            
            // Create command envelope with Protobuf payload
            var metadata = new Dictionary<string, string>
            {
                ["CommandTypeFull"] = command.GetType().FullName ?? command.GetType().Name,
                ["ExecutedAt"] = DateTime.UtcNow.ToString("O")
            };
            
            if (relatedEvent != null)
            {
                metadata["CausationId"] = relatedEvent.GetPayload()?.GetType().Name ?? string.Empty;
                metadata["RelatedEventId"] = relatedEvent.GetSortableUniqueId();
            }

            var envelope = await _envelopeService.CreateCommandEnvelope(command, partitionKeys, metadata);
            
            // Create actor ID with projector type
            var grainKey = $"{projectorType.Name}:{partitionKeys.ToPrimaryKeysString()}";
            var actorId = new ActorId($"{_options.ActorIdPrefix}:{grainKey}");
            
            _logger.LogDebug("Executing command {CommandType} on actor {ActorId}",
                command.GetType().Name, actorId.GetId());
            
            var aggregateActor = _actorProxyFactory.CreateActorProxy<IAggregateActor>(
                actorId,
                nameof(EnvelopeAggregateActor));

            // Execute command via actor
            var response = await aggregateActor.ExecuteCommandAsync(envelope);
            
            // Convert response back to Sekiban CommandResponse
            var commandResponse = await ConvertFromEnvelopeResponse(response, partitionKeys);
            
            return ResultBox<SekibanCommandResponse>.FromValue(commandResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute command {CommandType}", command.GetType().Name);
            return ResultBox<SekibanCommandResponse>.FromException(ex);
        }
    }

    public async Task<ResultBox<T>> QueryAsync<T>(IQueryCommon<T> query) where T : notnull
    {
        try
        {
            // For queries, we still use service invocation
            // but we could also implement query actors if needed
            return await _daprClient.InvokeMethodAsync<IQueryCommon<T>, ResultBox<T>>(
                _options.QueryServiceAppId,
                "query",
                query);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute query {QueryType}", query.GetType().Name);
            return ResultBox<T>.FromException(ex);
        }
    }

    public async Task<ResultBox<ListQueryResult<TResult>>> QueryAsync<TResult>(IListQueryCommon<TResult> query) where TResult : notnull
    {
        try
        {
            return await _daprClient.InvokeMethodAsync<IListQueryCommon<TResult>, ResultBox<ListQueryResult<TResult>>>(
                _options.QueryServiceAppId,
                "list-query",
                query);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute list query {QueryType}", query.GetType().Name);
            return ResultBox<ListQueryResult<TResult>>.FromException(ex);
        }
    }

    public async Task<ResultBox<Aggregate>> LoadAggregateAsync<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new()
    {
        try
        {
            // Create actor ID with projector type
            var projectorType = typeof(TAggregateProjector);
            var grainKey = $"{projectorType.Name}:{partitionKeys.ToPrimaryKeysString()}";
            var actorId = new ActorId($"{_options.ActorIdPrefix}:{grainKey}");
            
            var aggregateActor = _actorProxyFactory.CreateActorProxy<IAggregateActor>(
                actorId,
                nameof(EnvelopeAggregateActor));

            // Get the current state from the actor
            var stateJson = await aggregateActor.GetStateAsync();
            
            // Deserialize the JSON state
            var aggregate = JsonSerializer.Deserialize<Aggregate>(stateJson);
            if (aggregate == null)
            {
                return ResultBox<Aggregate>.FromException(
                    new InvalidOperationException("Failed to deserialize aggregate state"));
            }
            
            return ResultBox<Aggregate>.FromValue(aggregate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load aggregate for projector {ProjectorType}", typeof(TAggregateProjector).Name);
            return ResultBox<Aggregate>.FromException(ex);
        }
    }

    public SekibanDomainTypes GetDomainTypes() => _domainTypes;

    /// <summary>
    /// Converts envelope-based CommandResponse to Sekiban CommandResponse
    /// </summary>
    private async Task<SekibanCommandResponse> ConvertFromEnvelopeResponse(
        DaprCommandResponse envelopeResponse,
        PartitionKeys partitionKeys)
    {
        if (!envelopeResponse.IsSuccess)
        {
            // Parse error from JSON
            var errorMessage = "Command execution failed";
            if (!string.IsNullOrEmpty(envelopeResponse.ErrorJson))
            {
                try
                {
                    var errorData = JsonSerializer.Deserialize<Dictionary<string, object>>(envelopeResponse.ErrorJson);
                    errorMessage = errorData?.GetValueOrDefault("Message")?.ToString() ?? errorMessage;
                }
                catch
                {
                    errorMessage = envelopeResponse.ErrorJson;
                }
            }

            // For errors, we should throw an exception
            throw new InvalidOperationException(errorMessage);
        }

        // Extract events from payloads
        var events = new List<IEvent>();
        for (int i = 0; i < envelopeResponse.EventPayloads.Count; i++)
        {
            var eventType = envelopeResponse.EventTypes[i];
            var eventPayload = envelopeResponse.EventPayloads[i];
            
            // Create a temporary envelope for extraction
            var tempEnvelope = new EventEnvelope
            {
                EventType = eventType,
                EventPayload = eventPayload,
                AggregateId = partitionKeys.AggregateId.ToString(),
                PartitionId = partitionKeys.AggregateId,
                RootPartitionKey = partitionKeys.RootPartitionKey,
                Version = envelopeResponse.AggregateVersion - envelopeResponse.EventPayloads.Count + i + 1
            };
            
            var extractedEvents = await _envelopeService.ExtractEvents(new[] { tempEnvelope });
            if (extractedEvents.Any())
            {
                events.Add(extractedEvents.First());
            }
        }

        // Extract aggregate state if present
        Aggregate? aggregateState = null;
        if (envelopeResponse.AggregateStatePayload != null && envelopeResponse.AggregateStateType != null)
        {
            // For now, we'll deserialize from JSON
            // In a full implementation, this would use the Protobuf deserializer
            try
            {
                var json = System.Text.Encoding.UTF8.GetString(envelopeResponse.AggregateStatePayload);
                var payloadType = Type.GetType(envelopeResponse.AggregateStateType);
                if (payloadType != null)
                {
                    var payload = JsonSerializer.Deserialize(json, payloadType) as IAggregatePayload;
                    if (payload != null)
                    {
                        aggregateState = new Aggregate(
                            payload,
                            partitionKeys,
                            envelopeResponse.AggregateVersion,
                            events.LastOrDefault()?.GetSortableUniqueId() ?? string.Empty,
                            "1",
                            payloadType.Name,
                            payloadType.FullName ?? payloadType.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize aggregate state");
            }
        }

        return new SekibanCommandResponse(
            PartitionKeys: partitionKeys,
            Events: events,
            Version: envelopeResponse.AggregateVersion);
    }

    private PartitionKeys GetPartitionKeys(ICommandWithHandlerSerializable command)
    {
        var partitionKeysSpecifier = command.GetPartitionKeysSpecifier();
        var partitionKeys = partitionKeysSpecifier.DynamicInvoke(command) as PartitionKeys;
        
        if (partitionKeys is null)
        {
            throw new InvalidOperationException($"Failed to get partition keys for command type {command.GetType().Name}");
        }
        
        return partitionKeys;
    }

    private Type? GetProjectorTypeFromCommand(ICommandWithHandlerSerializable command)
    {
        var commandType = command.GetType();
        
        // Look for ICommandWithHandler<TCommand, TProjector> interface
        var commandInterface = commandType
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && 
                                i.GetGenericTypeDefinition() == typeof(ICommandWithHandler<,>));
        
        if (commandInterface != null)
        {
            // Get the projector type from the generic arguments
            return commandInterface.GetGenericArguments()[1];
        }

        // Look for ICommandWithHandler<TCommand, TProjector, TPayload> interface
        var commandWithPayloadInterface = commandType
            .GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && 
                                i.GetGenericTypeDefinition() == typeof(ICommandWithHandler<,,>));
        
        if (commandWithPayloadInterface != null)
        {
            // Get the projector type from the generic arguments
            return commandWithPayloadInterface.GetGenericArguments()[1];
        }

        return null;
    }
}