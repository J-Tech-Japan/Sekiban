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

namespace Sekiban.Pure.Dapr;

public class SekibanDaprExecutor : ISekibanExecutor
{
    private readonly DaprClient _daprClient;
    private readonly IActorProxyFactory _actorProxyFactory;
    private readonly SekibanDomainTypes _domainTypes;
    private readonly DaprSekibanOptions _options;

    public SekibanDaprExecutor(
        DaprClient daprClient,
        IActorProxyFactory actorProxyFactory,
        SekibanDomainTypes domainTypes,
        IOptions<DaprSekibanOptions> options)
    {
        _daprClient = daprClient;
        _actorProxyFactory = actorProxyFactory;
        _domainTypes = domainTypes;
        _options = options.Value;
    }

    public async Task<ResultBox<CommandResponse>> CommandAsync(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null)
    {
        try
        {
            // Get projector type from command
            var projectorType = GetProjectorTypeFromCommand(command);
            if (projectorType == null)
            {
                return ResultBox<CommandResponse>.FromException(
                    new InvalidOperationException($"Could not determine projector type for command {command.GetType().Name}"));
            }

            // Get partition keys
            var partitionKeys = GetPartitionKeys(command);
            
            // Create actor ID with projector type
            var grainKey = $"{projectorType.Name}:{partitionKeys.ToPrimaryKeysString()}";
            var actorId = new ActorId($"{_options.ActorIdPrefix}:{grainKey}");
            
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

            return await aggregateActor.ExecuteCommandAsync(command, metadata);
        }
        catch (Exception ex)
        {
            return ResultBox<CommandResponse>.FromException(ex);
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
            // Create actor ID with projector type
            var projectorType = typeof(TAggregateProjector);
            var grainKey = $"{projectorType.Name}:{partitionKeys.ToPrimaryKeysString()}";
            var actorId = new ActorId($"{_options.ActorIdPrefix}:{grainKey}");
            
            var aggregateActor = _actorProxyFactory.CreateActorProxy<IAggregateActor>(
                actorId,
                nameof(AggregateActor));

            // Get the current state from the actor
            var aggregate = await aggregateActor.GetStateAsync();
            
            return ResultBox<Aggregate>.FromValue(aggregate);
        }
        catch (Exception ex)
        {
            return ResultBox<Aggregate>.FromException(ex);
        }
    }


    private PartitionKeys GetPartitionKeys(ICommandWithHandlerSerializable command)
    {
        var partitionKeySpecifier = command.GetPartitionKeysSpecifier();
        var partitionKeys = partitionKeySpecifier.DynamicInvoke(command) as PartitionKeys;
        if (partitionKeys is null)
        {
            throw new InvalidOperationException($"GetPartitionKeys method not found for command type {command.GetType().Name}");
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