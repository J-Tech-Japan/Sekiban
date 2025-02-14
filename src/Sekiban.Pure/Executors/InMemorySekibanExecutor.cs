using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
using Sekiban.Pure.Repositories;
namespace Sekiban.Pure.Executors;

public class InMemorySekibanExecutor(
    SekibanDomainTypes sekibanDomainTypes,
    ICommandMetadataProvider metadataProvider,
    Repository repository) : ISekibanExecutor
{
    private readonly CommandExecutor _commandExecutor = new()
        { EventTypes = sekibanDomainTypes.EventTypes };

    public SekibanDomainTypes GetDomainTypes() => sekibanDomainTypes;
    public async Task<ResultBox<CommandResponse>> ExecuteCommandAsync(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null)
    {
        var partitionKeys = command.GetPartitionKeysSpecifier().DynamicInvoke(command) as PartitionKeys;
        if (partitionKeys is null)
        {
            return ResultBox<CommandResponse>.Error(new ApplicationException("Partition keys not found"));
        }
        return await sekibanDomainTypes.CommandTypes.ExecuteGeneral(
            _commandExecutor,
            command,
            partitionKeys,
            relatedEvent is null
                ? metadataProvider.GetMetadata()
                : metadataProvider.GetMetadataWithSubscribedEvent(relatedEvent),
            (pk, pj) => repository.Load(pk, pj).ToTask(),
            (_, events) => repository.Save(events).ToTask());
    }
    public async Task<ResultBox<TResult>> ExecuteQueryAsync<TResult>(IQueryCommon<TResult> queryCommon)
        where TResult : notnull
    {
        var projectorResult = sekibanDomainTypes.QueryTypes.GetMultiProjector(queryCommon);
        if (projectorResult.IsSuccess)
        {
            var projector = projectorResult.GetValue();
            var events = repository.Events;
            var projectionResult = events
                .ToResultBox()
                .ReduceEach(projector, (ev, proj) => sekibanDomainTypes.MultiProjectorsType.Project(proj, ev));
            if (projectionResult.IsSuccess)
            {
                var projection = projectionResult.GetValue();
                var lastEvent = events.LastOrDefault();
                var multiProjectionState = new MultiProjectionState(
                    projection,
                    lastEvent?.Id ?? Guid.Empty,
                    lastEvent?.SortableUniqueId ?? "",
                    events.Count,
                    0,
                    lastEvent?.PartitionKeys.RootPartitionKey ?? "default");
                var typedMultiProjectionState
                    = sekibanDomainTypes.MultiProjectorsType.ToTypedState(multiProjectionState);
                var queryExecutor = new QueryExecutor();
                var queryResult = await sekibanDomainTypes.QueryTypes.ExecuteAsQueryResult(
                    queryCommon,
                    selector => typedMultiProjectionState
                        .ToResultBox()
                        .ConveyorWrapTry(state => state)
                        .ToTask());
                return queryResult.ConveyorWrapTry(val => (TResult)val.GetValue());
            }
            return ResultBox<TResult>.Error(new ApplicationException("Projection failed"));
        }
        return ResultBox<TResult>.Error(new ApplicationException("Projector not found"));
    }
    public async Task<ResultBox<ListQueryResult<TResult>>> ExecuteQueryAsync<TResult>(
        IListQueryCommon<TResult> queryCommon)
        where TResult : notnull
    {
        var projectorResult = sekibanDomainTypes.QueryTypes.GetMultiProjector(queryCommon);
        if (projectorResult.IsSuccess)
        {
            var projector = projectorResult.GetValue();
            var events = repository.Events;
            var projectionResult = events
                .ToResultBox()
                .ReduceEach(projector, (ev, proj) => sekibanDomainTypes.MultiProjectorsType.Project(proj, ev));
            if (projectionResult.IsSuccess)
            {
                var projection = projectionResult.GetValue();
                var lastEvent = events.LastOrDefault();
                var multiProjectionState = new MultiProjectionState(
                    projection,
                    lastEvent?.Id ?? Guid.Empty,
                    lastEvent?.SortableUniqueId ?? "",
                    events.Count,
                    0,
                    lastEvent?.PartitionKeys.RootPartitionKey ?? "default");
                var typedMultiProjectionState
                    = sekibanDomainTypes.MultiProjectorsType.ToTypedState(multiProjectionState);
                var queryExecutor = new QueryExecutor();
                var queryResult = await sekibanDomainTypes.QueryTypes.ExecuteAsQueryResult(
                    queryCommon,
                    selector => typedMultiProjectionState
                        .ToResultBox()
                        .ConveyorWrapTry(state => state)
                        .ToTask());
                return queryResult.ConveyorWrapTry(val => (ListQueryResult<TResult>)val);
            }
            return ResultBox<ListQueryResult<TResult>>.Error(new ApplicationException("Projection failed"));
        }
        return ResultBox<ListQueryResult<TResult>>.Error(new ApplicationException("Projector not found"));
    }
    public Task<ResultBox<Aggregate>> LoadAggregateAsync<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new()
    {
        var events = repository.Events.Where(x => x.PartitionKeys == partitionKeys).ToList();
        return Aggregate.EmptyFromPartitionKeys(partitionKeys).Project(events, new TAggregateProjector()).ToTask();
    }
}