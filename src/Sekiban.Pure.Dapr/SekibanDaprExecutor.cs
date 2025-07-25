using Dapr;
using Dapr.Actors;
using Dapr.Actors.Client;
using Dapr.Client;
using Microsoft.Extensions.Options;
using ResultBoxes;
using Sekiban.Pure.Command;
using Sekiban.Pure.Dapr.Actors;
using Sekiban.Pure.Dapr.Configuration;
using Sekiban.Pure.Dapr.Parts;
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
using System.Linq;
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
            // Get partition keys
            var partitionKeys = GetPartitionKeys(command);
            
            // Get projector from command (matching Orleans pattern)
            var projector = command.GetProjector();
            var partitionKeyAndProjector = new PartitionKeysAndProjector(partitionKeys, projector);
            
            // Create actor ID using the correct grain key format (matching Orleans pattern)
            var actorId = new ActorId(partitionKeyAndProjector.ToProjectorGrainKey());
            
            // Debug: Print actor ID for troubleshooting
            Console.WriteLine($"[DEBUG] Creating ActorProxy with ActorId: {actorId.GetId()}, ActorType: {nameof(AggregateActor)}");
            Console.WriteLine($"[DEBUG] ProjectorType: {projector.GetType().Name}, PartitionKeys: {partitionKeys.ToPrimaryKeysString()}");
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
            // Create SerializableCommandAndMetadata
            var commandAndMetadata = await SerializableCommandAndMetadata.CreateFromAsync(
                command,
                metadata,
                _domainTypes.JsonSerializerOptions);
            
            // Execute command via SerializableCommandAndMetadata
            // Retry once if we get a 500 error (actor not yet created)
            SerializableCommandResponse envelopeResponse;
            try
            {
                envelopeResponse = await aggregateActor.ExecuteCommandAsync(commandAndMetadata);
            }
            catch (DaprApiException ex) when (ex.Message.Contains("500"))
            {
                // Actor might not exist yet, retry after a short delay
                await Task.Delay(100);
                envelopeResponse = await aggregateActor.ExecuteCommandAsync(commandAndMetadata);
            }
            
            // Convert SerializableCommandResponse back to SekibanCommandResponse
            var responseResult = await envelopeResponse.ToCommandResponseAsync(_domainTypes);
            
                if (!responseResult.HasValue || responseResult.Value is null)
            {
                return ResultBox<SekibanCommandResponse>.FromException(
                    new InvalidOperationException("Failed to convert command response"));
            }
            
            return ResultBox<SekibanCommandResponse>.FromValue(responseResult.Value);
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
            // Check if this is a multi-projection query
            var projectorResult = _domainTypes.QueryTypes.GetMultiProjector(query);
            if (projectorResult.IsSuccess)
            {
                // Get the appropriate multi-projector name
                var projectorNameResult = _domainTypes.MultiProjectorsType.GetMultiProjectorNameFromMultiProjector(
                    projectorResult.GetValue());
                
                if (!projectorNameResult.IsSuccess)
                {
                    return ResultBox<T>.FromException(projectorNameResult.GetException());
                }
                
                var projectorName = projectorNameResult.GetValue();
                var actorId = new ActorId(projectorName);
                var actor = _actorProxyFactory.CreateActorProxy<IMultiProjectorActor>(
                    actorId, 
                    nameof(MultiProjectorActor));
                
                // Wait for sortable unique ID if needed
                if (query is IWaitForSortableUniqueId waitForQuery && 
                    !string.IsNullOrEmpty(waitForQuery.WaitForSortableUniqueId))
                {
                    var sortableUniqueId = waitForQuery.WaitForSortableUniqueId;
                    var timeoutMs = 30000; // 30 seconds timeout
                    var pollingIntervalMs = 50; // Poll every 50ms for faster response
                    
                    var stopwatch = new System.Diagnostics.Stopwatch();
                    stopwatch.Start();
                    
                    while (stopwatch.ElapsedMilliseconds < timeoutMs)
                    {
                        var isReceived = await actor.IsSortableUniqueIdReceived(sortableUniqueId);
                        if (isReceived)
                        {
                            break;
                        }
                        
                        await Task.Delay(pollingIntervalMs);
                    }
                }
                
                // Convert query to serializable format
                var serializableQuery = await SerializableQuery.CreateFromAsync(query, _domainTypes.JsonSerializerOptions);
                var serializableResult = await actor.QueryAsync(serializableQuery);
                
                // Convert back to ResultBox<object>
                var resultBox = await serializableResult.ToResultBoxAsync(_domainTypes);
                
                if (!resultBox.IsSuccess)
                {
                    return ResultBox<T>.FromException(resultBox.GetException());
                }
                
                return ResultBox<T>.FromValue((T)resultBox.GetValue());
            }
            
            // For other queries, delegate to a service
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
            // Check if this is a multi-projection query
            var projectorResult = _domainTypes.QueryTypes.GetMultiProjector(query);
            if (projectorResult.IsSuccess)
            {
                // Get the appropriate multi-projector name
                var projectorNameResult = _domainTypes.MultiProjectorsType.GetMultiProjectorNameFromMultiProjector(
                    projectorResult.GetValue());
                
                if (!projectorNameResult.IsSuccess)
                {
                    return ResultBox<ListQueryResult<TResult>>.FromException(projectorNameResult.GetException());
                }
                
                var projectorName = projectorNameResult.GetValue();
                var actorId = new ActorId(projectorName);
                var actor = _actorProxyFactory.CreateActorProxy<IMultiProjectorActor>(
                    actorId, 
                    nameof(MultiProjectorActor));
                
                // Wait for sortable unique ID if needed
                if (query is IWaitForSortableUniqueId waitForQuery && 
                    !string.IsNullOrEmpty(waitForQuery.WaitForSortableUniqueId))
                {
                    var sortableUniqueId = waitForQuery.WaitForSortableUniqueId;
                    var timeoutMs = 30000; // 30 seconds timeout
                    var pollingIntervalMs = 50; // Poll every 50ms for faster response
                    
                    var stopwatch = new System.Diagnostics.Stopwatch();
                    stopwatch.Start();
                    
                    while (stopwatch.ElapsedMilliseconds < timeoutMs)
                    {
                        var isReceived = await actor.IsSortableUniqueIdReceived(sortableUniqueId);
                        if (isReceived)
                        {
                            break;
                        }
                        
                        await Task.Delay(pollingIntervalMs);
                    }
                }
                
                // Convert query to serializable format
                var serializableQuery = await SerializableListQuery.CreateFromAsync(query, _domainTypes.JsonSerializerOptions);
                var serializableResult = await actor.QueryListAsync(serializableQuery);
                
                // Convert back to ResultBox<IListQueryResult>
                var resultBox = await serializableResult.ToResultBoxAsync(_domainTypes);
                
                if (!resultBox.IsSuccess)
                {
                    return ResultBox<ListQueryResult<TResult>>.FromException(resultBox.GetException());
                }
                
                // Convert IListQueryResult to ListQueryResult<TResult>
                var listResult = resultBox.GetValue();
                if (listResult is ListQueryResult<TResult> typedResult)
                {
                    return ResultBox<ListQueryResult<TResult>>.FromValue(typedResult);
                }
                
                // If it's a generic result, convert it
                var generalResult = listResult.ToGeneral(query);
                return ListQueryResult<TResult>.FromGeneral(generalResult);
            }
            
            // For other queries, delegate to a service
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
            // Create PartitionKeysAndProjector (matching Orleans pattern)
            var partitionKeyAndProjector = new PartitionKeysAndProjector(partitionKeys, new TAggregateProjector());
            
            // Create actor ID using the correct grain key format (matching Orleans pattern)
            var actorId = new ActorId(partitionKeyAndProjector.ToProjectorGrainKey());
            
            // Debug: Print actor ID for troubleshooting
            Console.WriteLine($"[DEBUG] LoadAggregate - Creating ActorProxy with ActorId: {actorId.GetId()}, ActorType: {nameof(AggregateActor)}");
            Console.WriteLine($"[DEBUG] LoadAggregate - ProjectorType: {typeof(TAggregateProjector).Name}, PartitionKeys: {partitionKeys.ToPrimaryKeysString()}");
            Console.WriteLine($"[DEBUG] LoadAggregate - AggregateId: {partitionKeys.AggregateId}");
            
            var aggregateActor = _actorProxyFactory.CreateActorProxy<IAggregateActor>(
                actorId,
                nameof(AggregateActor));

            // Get the current state from the actor as SerializableAggregate
            var serializableAggregate = await aggregateActor.GetAggregateStateAsync();
            
            // Convert SerializableAggregate back to Aggregate
            var aggregateOptional = await serializableAggregate.ToAggregateAsync(_domainTypes);
            
            if (!aggregateOptional.HasValue)
            {
                return ResultBox<Aggregate>.FromException(
                    new InvalidOperationException($"Failed to deserialize aggregate from SerializableAggregate"));
            }
            
            return ResultBox<Aggregate>.FromValue(aggregateOptional.Value!);
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
}
