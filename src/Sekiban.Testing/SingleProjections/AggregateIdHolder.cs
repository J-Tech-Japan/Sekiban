using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Testing.SingleProjections;

public class AggregateIdHolder<TBaseAggregatePayload> : IAggregateIdHolder
    where TBaseAggregatePayload : IAggregatePayloadCommon
{
    private readonly IAggregateLoader aggregateLoader;
    public AggregateIdHolder(IAggregateLoader aggregateLoader)
    {
        this.aggregateLoader = aggregateLoader;
    }

    public Guid AggregateId
    {
        get;
        set;
    } = Guid.Empty;

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
        return GetAggregate()?.GetPayloadTypeIs<TAggregatePayload>() ?? false;
    }
    public async Task<Aggregate<TBaseAggregatePayload>?> GetAggregateAsync()
    {
        return await aggregateLoader.AsAggregateAsync<TBaseAggregatePayload>(AggregateId);
    }
    public Aggregate<TBaseAggregatePayload>? GetAggregate()
    {
        return GetAggregateAsync().Result;
    }
}
