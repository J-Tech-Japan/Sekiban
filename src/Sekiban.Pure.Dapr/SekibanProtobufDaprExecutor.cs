using Dapr.Actors;
using Dapr.Actors.Client;
using Dapr.Client;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Dapr.Actors;
using Sekiban.Pure.Dapr.Configuration;
using Sekiban.Pure.Dapr.Protos;
using Sekiban.Pure.Dapr.Serialization;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
using Microsoft.Extensions.Logging;
using SekibanCommandResponse = Sekiban.Pure.Command.Executor.CommandResponse;

namespace Sekiban.Pure.Dapr;

/// <summary>
/// Protobuf-enabled Sekiban executor for Dapr that communicates with actors using Protobuf messages
/// </summary>
public class SekibanProtobufDaprExecutor : ISekibanExecutor
{
    private readonly DaprClient _daprClient;
    private readonly IActorProxyFactory _actorProxyFactory;
    private readonly SekibanDomainTypes _domainTypes;
    private readonly IDaprProtobufSerializationService _serialization;
    private readonly DaprSekibanOptions _options;
    private readonly ILogger<SekibanProtobufDaprExecutor> _logger;

    public SekibanProtobufDaprExecutor(
        DaprClient daprClient,
        IActorProxyFactory actorProxyFactory,
        SekibanDomainTypes domainTypes,
        IDaprProtobufSerializationService serialization,
        IOptions<DaprSekibanOptions> options,
        ILogger<SekibanProtobufDaprExecutor> logger)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _actorProxyFactory = actorProxyFactory ?? throw new ArgumentNullException(nameof(actorProxyFactory));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _serialization = serialization ?? throw new ArgumentNullException(nameof(serialization));
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
            
            // Create actor ID with projector type
            var grainKey = $"{projectorType.Name}:{partitionKeys.ToPrimaryKeysString()}";
            var actorId = new ActorId($"{_options.ActorIdPrefix}:{grainKey}");
            
            // Create protobuf-enabled actor proxy
            var protobufActor = _actorProxyFactory.CreateActorProxy<IProtobufAggregateActor>(
                actorId,
                nameof(ProtobufAggregateActor));

            // Serialize command to protobuf
            var commandEnvelope = await _serialization.SerializeCommandToProtobufAsync(command);
            commandEnvelope.PartitionKey = partitionKeys.ToPrimaryKeysString();
            
            // Create command request
            var request = new ExecuteCommandRequest
            {
                Command = commandEnvelope,
                RelatedEventId = relatedEvent?.GetSortableUniqueId() ?? string.Empty
            };

            // Execute command via protobuf
            var responseBytes = await protobufActor.ExecuteCommandAsync(request.ToByteArray());
            
            // Deserialize response
            var protobufResponse = ProtobufCommandResponse.Parser.ParseFrom(responseBytes);
            
            // Convert to CommandResponse
            var response = await ConvertProtobufResponseAsync(protobufResponse, partitionKeys);
            
            return ResultBox<SekibanCommandResponse>.FromValue(response);
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
            // Create protobuf query request
            var queryEnvelope = new ProtobufQueryRequest
            {
                QueryJson = ByteString.CopyFrom(await _serialization.SerializeAsync(query)),
                QueryType = query.GetType().FullName ?? query.GetType().Name
            };
            
            // Execute query via service invocation
            var responseBytes = await _daprClient.InvokeMethodAsync<byte[], byte[]>(
                _options.QueryServiceAppId,
                "protobuf-query",
                queryEnvelope.ToByteArray());
            
            // Deserialize response
            var queryResponse = ProtobufQueryResponse.Parser.ParseFrom(responseBytes);
            
            if (!queryResponse.Success)
            {
                return ResultBox<T>.FromException(new InvalidOperationException(queryResponse.ErrorMessage));
            }
            
            // Deserialize result
            var result = await _serialization.DeserializeAsync<T>(queryResponse.ResultJson.ToByteArray());
            if (result == null)
            {
                return ResultBox<T>.FromException(new InvalidOperationException("Failed to deserialize query result"));
            }
            
            return ResultBox<T>.FromValue(result);
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
            // Create protobuf query request
            var queryEnvelope = new ProtobufQueryRequest
            {
                QueryJson = ByteString.CopyFrom(await _serialization.SerializeAsync(query)),
                QueryType = query.GetType().FullName ?? query.GetType().Name
            };
            
            // Execute query via service invocation
            var responseBytes = await _daprClient.InvokeMethodAsync<byte[], byte[]>(
                _options.QueryServiceAppId,
                "protobuf-list-query",
                queryEnvelope.ToByteArray());
            
            // Deserialize response
            var queryResponse = ProtobufQueryResponse.Parser.ParseFrom(responseBytes);
            
            if (!queryResponse.Success)
            {
                return ResultBox<ListQueryResult<TResult>>.FromException(new InvalidOperationException(queryResponse.ErrorMessage));
            }
            
            // Deserialize result
            var result = await _serialization.DeserializeAsync<ListQueryResult<TResult>>(queryResponse.ResultJson.ToByteArray());
            if (result == null)
            {
                return ResultBox<ListQueryResult<TResult>>.FromException(new InvalidOperationException("Failed to deserialize query result"));
            }
            
            return ResultBox<ListQueryResult<TResult>>.FromValue(result);
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
            
            var protobufActor = _actorProxyFactory.CreateActorProxy<IProtobufAggregateActor>(
                actorId,
                nameof(ProtobufAggregateActor));

            // Get the current state from the actor as protobuf
            var stateBytes = await protobufActor.GetStateAsync();
            
            // Deserialize from protobuf
            var protobufEnvelope = ProtobufAggregateEnvelope.Parser.ParseFrom(stateBytes);
            var aggregate = await _serialization.DeserializeAggregateFromProtobufAsync(protobufEnvelope);
            
            if (aggregate == null)
            {
                return ResultBox<Aggregate>.FromException(new InvalidOperationException("Failed to load aggregate"));
            }
            
            return ResultBox<Aggregate>.FromValue(aggregate as Aggregate ?? 
                throw new InvalidOperationException("Loaded aggregate is not of the expected type"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load aggregate for projector {ProjectorType}", typeof(TAggregateProjector).Name);
            return ResultBox<Aggregate>.FromException(ex);
        }
    }

    public SekibanDomainTypes GetDomainTypes() => _domainTypes;

    /// <summary>
    /// Converts a Protobuf command response to the standard CommandResponse
    /// </summary>
    private async Task<SekibanCommandResponse> ConvertProtobufResponseAsync(ProtobufCommandResponse protobufResponse, PartitionKeys partitionKeys)
    {
        if (!protobufResponse.Success)
        {
            // For errors, we need to throw an exception as SekibanCommandResponse only contains success data
            throw new InvalidOperationException(protobufResponse.ErrorMessage);
        }

        // Deserialize aggregate if present
        Aggregate? aggregate = null;
        if (protobufResponse.Aggregate != null)
        {
            var aggregateResult = await _serialization.DeserializeAggregateFromProtobufAsync(protobufResponse.Aggregate);
            aggregate = aggregateResult as Aggregate;
        }

        // Deserialize events
        var events = new List<IEvent>();
        foreach (var eventEnvelope in protobufResponse.Events)
        {
            var @event = await _serialization.DeserializeEventFromProtobufAsync(eventEnvelope);
            if (@event != null)
            {
                events.Add(@event);
            }
        }

        return new SekibanCommandResponse(
            PartitionKeys: partitionKeys,
            Events: events,
            Version: aggregate?.Version ?? 0);
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