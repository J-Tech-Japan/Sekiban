using Dapr.Actors;
using Dapr.Actors.Client;
using Dapr.Client;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Pure.Command;
using Sekiban.Pure.Dapr.Actors;
using Sekiban.Pure.Dapr.Configuration;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Exceptions;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
using Sekiban.Pure;
using Sekiban.Pure.Dapr.Serialization;
using System.Text.Json;
using SekibanCommandResponse = Sekiban.Pure.Command.Executor.CommandResponse;

namespace Sekiban.Pure.Dapr;

public class SekibanDaprExecutor : ISekibanExecutor
{
    private readonly DaprClient _daprClient;
    private readonly IActorProxyFactory _actorProxyFactory;
    private readonly SekibanDomainTypes _domainTypes;
    private readonly IDaprSerializationService _serialization;
    private readonly DaprSekibanOptions _options;

    public SekibanDaprExecutor(
        DaprClient daprClient,
        IActorProxyFactory actorProxyFactory,
        SekibanDomainTypes domainTypes,
        IDaprSerializationService serialization,
        IOptions<DaprSekibanOptions> options)
    {
        _daprClient = daprClient;
        _actorProxyFactory = actorProxyFactory;
        _domainTypes = domainTypes;
        _serialization = serialization;
        _options = options.Value;
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
            
            // Create actor ID using partition keys primary string
            var actorId = new ActorId(partitionKeys.ToPrimaryKeysString());
            
            // Debug: Print actor ID for troubleshooting
            Console.WriteLine($"[DEBUG] Creating ActorProxy with ActorId: {actorId.GetId()}, ActorType: {nameof(AggregateActor)}");
            Console.WriteLine($"[DEBUG] ProjectorType: {projectorType.Name}, PartitionKeys: {partitionKeys.ToPrimaryKeysString()}");
            Console.WriteLine($"[DEBUG] AggregateId: {partitionKeys.AggregateId}");
            
            var aggregateActor = _actorProxyFactory.CreateActorProxy<IAggregateActor>(
                actorId,
                nameof(AggregateActor));

            // Create command metadata
            var commandId = Guid.NewGuid();
            var metadata = new CommandMetadata(
                CommandId: commandId,
                CausationId: relatedEvent?.GetPayload()?.GetType().Name ?? string.Empty,
                CorrelationId: commandId.ToString(),
                ExecutedUser: "system");

            // Create a command envelope for the new interface
            var envelope = new CommandEnvelope(
                commandType: command.GetType().AssemblyQualifiedName ?? command.GetType().FullName ?? command.GetType().Name,
                commandPayload: System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command)),
                aggregateId: partitionKeys.AggregateId.ToString(),
                partitionId: partitionKeys.AggregateId,
                rootPartitionKey: partitionKeys.RootPartitionKey,
                metadata: new Dictionary<string, string>
                {
                    ["CausationId"] = metadata.CausationId,
                    ["ExecutedUser"] = metadata.ExecutedUser
                },
                correlationId: metadata.CorrelationId);
            
            // Execute command via envelope
            var envelopeResponse = await aggregateActor.ExecuteCommandAsync(envelope);
            
            // Convert back to SekibanCommandResponse
            if (!envelopeResponse.IsSuccess)
            {
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
                
                // For errors, we need to throw an exception as SekibanCommandResponse only contains success data
                return ResultBox<SekibanCommandResponse>.FromException(
                    new InvalidOperationException(errorMessage));
            }
            
            // Extract events from payloads
            var events = new List<IEvent>();
            for (int i = 0; i < envelopeResponse.EventPayloads.Count; i++)
            {
                var eventJson = System.Text.Encoding.UTF8.GetString(envelopeResponse.EventPayloads[i]);
                var eventType = Type.GetType(envelopeResponse.EventTypes[i]);
                if (eventType != null)
                {
                    var @event = JsonSerializer.Deserialize(eventJson, eventType) as IEvent;
                    if (@event != null)
                    {
                        events.Add(@event);
                    }
                }
            }
            
            // Extract aggregate state if present
            Aggregate? aggregateState = null;
            if (envelopeResponse.AggregateStatePayload != null && envelopeResponse.AggregateStateType != null)
            {
                try
                {
                    var json = System.Text.Encoding.UTF8.GetString(envelopeResponse.AggregateStatePayload);
                    aggregateState = JsonSerializer.Deserialize<Aggregate>(json);
                }
                catch (Exception ex)
                {
                    // Log but don't fail
                    Console.WriteLine($"Failed to deserialize aggregate state: {ex.Message}");
                }
            }
            
            return ResultBox<SekibanCommandResponse>.FromValue(
                new SekibanCommandResponse(
                    PartitionKeys: partitionKeys,
                    Events: events,
                    Version: envelopeResponse.AggregateVersion));
        }
        catch (Exception ex)
        {
            return ResultBox<SekibanCommandResponse>.FromException(ex);
        }
    }

    public async Task<ResultBox<T>> QueryAsync<T>(IQueryCommon<T> query) where T : notnull
    {
        try
        {
            // For now, delegate all queries to a service
            return await _daprClient.InvokeMethodAsync<IQueryCommon<T>, ResultBox<T>>(
                _options.QueryServiceAppId,
                "query",
                query);
        }
        catch (Exception ex)
        {
            return ResultBox<T>.FromException(ex);
        }
    }


    public SekibanDomainTypes GetDomainTypes() => _domainTypes;

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
            return ResultBox<ListQueryResult<TResult>>.FromException(ex);
        }
    }

    public async Task<ResultBox<Aggregate>> LoadAggregateAsync<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new()
    {
        try
        {
            // Create actor ID using partition keys primary string
            var actorId = new ActorId(partitionKeys.ToPrimaryKeysString());
            
            // Debug: Print actor ID for troubleshooting
            Console.WriteLine($"[DEBUG] LoadAggregate - Creating ActorProxy with ActorId: {actorId.GetId()}, ActorType: {nameof(AggregateActor)}");
            Console.WriteLine($"[DEBUG] LoadAggregate - ProjectorType: {typeof(TAggregateProjector).Name}, PartitionKeys: {partitionKeys.ToPrimaryKeysString()}");
            Console.WriteLine($"[DEBUG] LoadAggregate - AggregateId: {partitionKeys.AggregateId}");
            
            var aggregateActor = _actorProxyFactory.CreateActorProxy<IAggregateActor>(
                actorId,
                nameof(AggregateActor));

            // Get the current state from the actor as JSON
            var stateJson = await aggregateActor.GetStateAsync();
            
            // Deserialize the JSON state using SerializableAggregate
            var serializableAggregate = JsonSerializer.Deserialize<SerializableAggregate>(stateJson);
            if (serializableAggregate == null)
            {
                return ResultBox<Aggregate>.FromException(
                    new InvalidOperationException("Failed to deserialize aggregate state"));
            }
            
            // Convert SerializableAggregate to Aggregate
            var aggregateOptional = await serializableAggregate.ToAggregateAsync(_domainTypes);
            if (!aggregateOptional.HasValue)
            {
                return ResultBox<Aggregate>.FromException(
                    new InvalidOperationException("Failed to convert SerializableAggregate to Aggregate"));
            }
            
            return ResultBox<Aggregate>.FromValue(aggregateOptional.Value);
        }
        catch (Exception ex)
        {
            return ResultBox<Aggregate>.FromException(ex);
        }
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