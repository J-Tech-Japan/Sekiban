using Sekiban.Core.Aggregate;
namespace Sekiban.Testing.SingleProjections;

public interface IAggregateIdHolder
{
    public Guid AggregateId { get; set; }

    public string GetRootPartitionKey();

    public Task<AggregateState<TAggregatePayload>?> GetAggregateStateAsync<TAggregatePayload>() where TAggregatePayload : IAggregatePayloadCommon;
    public AggregateState<TAggregatePayload>? GetAggregateState<TAggregatePayload>() where TAggregatePayload : IAggregatePayloadCommon;
    public bool IsAggregateType<TAggregatePayload>() where TAggregatePayload : IAggregatePayloadCommon;
}
