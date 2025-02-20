using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
namespace Sekiban.Pure.Orleans.Parts;

public class SekibanOrleansExecutor(
    IClusterClient clusterClient,
    SekibanDomainTypes sekibanDomainTypes,
    ICommandMetadataProvider metadataProvider) : ISekibanExecutor
{

    public SekibanDomainTypes GetDomainTypes() => sekibanDomainTypes;
    public async Task<ResultBox<CommandResponse>> CommandAsync(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null)
    {
        var partitionKeySpecifier = command.GetPartitionKeysSpecifier();
        var partitionKeys = partitionKeySpecifier.DynamicInvoke(command) as PartitionKeys;
        if (partitionKeys is null)
            return ResultBox<CommandResponse>.Error(new ApplicationException("Partition keys can not be found"));
        var projector = command.GetProjector();
        var partitionKeyAndProjector = new PartitionKeysAndProjector(partitionKeys, projector);
        var aggregateProjectorGrain =
            clusterClient.GetGrain<IAggregateProjectorGrain>(partitionKeyAndProjector.ToProjectorGrainKey());
        var toReturn = await aggregateProjectorGrain.ExecuteCommandAsync(
            command,
            metadataProvider.GetMetadata());
        return toReturn;
    }
    public async Task<ResultBox<TResult>> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon)
        where TResult : notnull
    {
        var projectorResult = sekibanDomainTypes.QueryTypes.GetMultiProjector(queryCommon);
        if (!projectorResult.IsSuccess)
            return ResultBox<TResult>.Error(new ApplicationException("Projector not found"));
        var nameResult
            = sekibanDomainTypes.MultiProjectorsType
                .GetMultiProjectorNameFromMultiProjector(projectorResult.GetValue());
        if (!nameResult.IsSuccess)
            return ResultBox<TResult>.Error(new ApplicationException("Projector name not found"));
        var multiProjectorGrain
            = clusterClient.GetGrain<IMultiProjectorGrain>(nameResult.GetValue());
        var result = await multiProjectorGrain.QueryAsync(queryCommon);
        return result.ToResultBox().Remap(a => a.GetValue()).Cast<TResult>();
    }
    public async Task<ResultBox<ListQueryResult<TResult>>> QueryAsync<TResult>(
        IListQueryCommon<TResult> queryCommon)
    {
        var projectorResult = sekibanDomainTypes.QueryTypes.GetMultiProjector(queryCommon);
        if (!projectorResult.IsSuccess)
            return ResultBox<ListQueryResult<TResult>>.Error(new ApplicationException("Projector not found"));
        var nameResult
            = sekibanDomainTypes.MultiProjectorsType
                .GetMultiProjectorNameFromMultiProjector(projectorResult.GetValue());
        if (!nameResult.IsSuccess)
            return ResultBox<ListQueryResult<TResult>>.Error(new ApplicationException("Projector name not found"));
        var multiProjectorGrain
            = clusterClient.GetGrain<IMultiProjectorGrain>(nameResult.GetValue());
        var result = await multiProjectorGrain.QueryAsync(queryCommon);
        return result.ToResultBox().Cast<IListQueryResult, ListQueryResult<TResult>>();
    }
    public async Task<ResultBox<Aggregate>> LoadAggregateAsync<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new()
    {
        var partitionKeyAndProjector = new PartitionKeysAndProjector(partitionKeys, new TAggregateProjector());

        var aggregateProjectorGrain =
            clusterClient.GetGrain<IAggregateProjectorGrain>(partitionKeyAndProjector.ToProjectorGrainKey());
        var state = await aggregateProjectorGrain.GetStateAsync();
        return state;
    }
}

