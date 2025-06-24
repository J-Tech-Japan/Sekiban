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
            var partitionKeys = GetPartitionKeys(command);
            var actorId = new ActorId($"{_options.ActorIdPrefix}:{partitionKeys.ToPrimaryKeysString()}");
            
            var aggregateActor = _actorProxyFactory.CreateActorProxy<IAggregateActor>(
                actorId,
                nameof(AggregateActor));

            return await aggregateActor.ExecuteCommandAsync(command, relatedEvent);
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
            var actorId = new ActorId($"{_options.ActorIdPrefix}:{partitionKeys.ToPrimaryKeysString()}");
            var aggregateActor = _actorProxyFactory.CreateActorProxy<IAggregateActor>(
                actorId,
                nameof(AggregateActor));

            var eventsResult = await aggregateActor.GetEventsAsync();
            if (!eventsResult.IsSuccess)
            {
                return ResultBox<Aggregate>.FromException(eventsResult.GetException());
            }

            var events = eventsResult.GetValue().ToList();
            var projector = new TAggregateProjector();
            var initialPayload = new Sekiban.Pure.Aggregates.EmptyAggregatePayload();
            var payload = projector.Project(initialPayload, null!);
            
            foreach (var evt in events)
            {
                payload = projector.Project(payload, evt);
            }
            
            return ResultBox<Aggregate>.FromValue(
                Aggregate.FromPayload(
                    payload,
                    partitionKeys,
                    events.Count,
                    events.LastOrDefault()?.SortableUniqueId ?? string.Empty,
                    projector));
        }
        catch (Exception ex)
        {
            return ResultBox<Aggregate>.FromException(ex);
        }
    }


    private PartitionKeys GetPartitionKeys(ICommandWithHandlerSerializable command)
    {
        var commandType = command.GetType();
        var method = commandType.GetMethod("GetPartitionKeys", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (method != null)
        {
            return (PartitionKeys)method.Invoke(null, new object[] { command })!;
        }

        throw new InvalidOperationException($"GetPartitionKeys method not found for command type {commandType.Name}");
    }
}