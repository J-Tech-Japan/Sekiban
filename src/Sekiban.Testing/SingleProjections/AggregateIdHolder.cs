using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Testing.SingleProjections;

/// <summary>
///     Internal Class for managing the AggregateId and RootPartitionKey
/// </summary>
/// <typeparam name="TBaseAggregatePayload"></typeparam>
public class AggregateIdHolder<TBaseAggregatePayload>(IAggregateLoader aggregateLoader) : IAggregateIdHolder
    where TBaseAggregatePayload : IAggregatePayloadCommon
{
    /// <summary>
    ///     Current AggregateId
    /// </summary>
    public Guid AggregateId { get; set; } = Guid.Empty;
    /// <summary>
    ///     Current RootPartitionKey
    /// </summary>
    public string RootPartitionKey { get; set; } = IDocument.DefaultRootPartitionKey;
    /// <summary>
    ///     Get RootPartitionKey
    /// </summary>
    /// <returns></returns>
    public string GetRootPartitionKey() => RootPartitionKey;
    /// <summary>
    ///     Get current Aggregate State
    /// </summary>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public async Task<AggregateState<TAggregatePayload>?> GetAggregateStateAsync<TAggregatePayload>()
        where TAggregatePayload : IAggregatePayloadCommon
    {
        var aggregate = await GetAggregateAsync();
        return aggregate?.GetPayloadTypeIs<TAggregatePayload>() == true ? aggregate.ToState<TAggregatePayload>() : null;
    }
    /// <summary>
    ///     Get current Aggregate state as specific type.
    ///     It is useful when you use subtypes of AggregatePayload.
    /// </summary>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public AggregateState<TAggregatePayload>? GetAggregateState<TAggregatePayload>() where TAggregatePayload : IAggregatePayloadCommon
    {
        var aggregate = GetAggregate();
        return aggregate?.GetPayloadTypeIs<TAggregatePayload>() == true ? aggregate.ToState<TAggregatePayload>() : null;
    }
    /// <summary>
    ///     Check Aggregate type is specific type.
    ///     It is useful when you use subtypes of AggregatePayload.
    /// </summary>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public bool IsAggregateType<TAggregatePayload>() where TAggregatePayload : IAggregatePayloadCommon
    {
        var aggregate = GetAggregate();
        return aggregate?.GetPayloadTypeIs<TAggregatePayload>() ?? false;
    }
    /// <summary>
    ///     Get aggregate using BaseAggregatePayload
    /// </summary>
    /// <returns></returns>
    public async Task<Aggregate<TBaseAggregatePayload>?> GetAggregateAsync() =>
        await aggregateLoader.AsAggregateAsync<TBaseAggregatePayload>(AggregateId, RootPartitionKey);
    public Aggregate<TBaseAggregatePayload>? GetAggregate() => GetAggregateAsync().Result;
}
