using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Testing.SingleProjections;

public class AggregateIdHolder<TBaseAggregatePayload> : IAggregateIdHolder where TBaseAggregatePayload : IAggregatePayloadCommon
{
    private readonly IAggregateLoader aggregateLoader;
    public AggregateIdHolder(IAggregateLoader aggregateLoader) => this.aggregateLoader = aggregateLoader;

    public Guid AggregateId
    {
        get;
        set;
    } = Guid.Empty;
    public string RootPartitionKey
    {
        get;
        set;
    } = IDocument.DefaultRootPartitionKey;

    public string GetRootPartitionKey() => RootPartitionKey;
    public async Task<AggregateState<TAggregatePayload>?> GetAggregateStateAsync<TAggregatePayload>()
        where TAggregatePayload : IAggregatePayloadCommon
    {
        var aggregate = await GetAggregateAsync();
        return aggregate?.GetPayloadTypeIs<TAggregatePayload>() == true ? aggregate.ToState<TAggregatePayload>() : null;
    }
    public AggregateState<TAggregatePayload>? GetAggregateState<TAggregatePayload>() where TAggregatePayload : IAggregatePayloadCommon
    {
        var aggregate = GetAggregate();
        return aggregate?.GetPayloadTypeIs<TAggregatePayload>() == true ? aggregate.ToState<TAggregatePayload>() : null;
    }
    public bool IsAggregateType<TAggregatePayload>() where TAggregatePayload : IAggregatePayloadCommon
    {
        var aggregate = GetAggregate();
        return aggregate?.GetPayloadTypeIs<TAggregatePayload>() ?? false;
    }
    public async Task<Aggregate<TBaseAggregatePayload>?> GetAggregateAsync() =>
        await aggregateLoader.AsAggregateAsync<TBaseAggregatePayload>(AggregateId, RootPartitionKey);
    public Aggregate<TBaseAggregatePayload>? GetAggregate() => GetAggregateAsync().Result;
}
