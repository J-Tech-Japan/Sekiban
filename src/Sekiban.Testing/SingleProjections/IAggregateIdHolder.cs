using Sekiban.Core.Aggregate;
namespace Sekiban.Testing.SingleProjections;

/// <summary>
///     Manages the AggregateId and RootPartitionKey
/// </summary>
public interface IAggregateIdHolder
{
    /// <summary>
    ///     Current Aggregate Id
    /// </summary>
    public Guid AggregateId { get; set; }
    /// <summary>
    ///     Current Aggregate RootPartitionKey
    /// </summary>
    public string RootPartitionKey { get; set; }
    /// <summary>
    ///     Get current Aggregate RootPartitionKey
    /// </summary>
    /// <returns></returns>
    public string GetRootPartitionKey() => RootPartitionKey;
    /// <summary>
    ///     Get current Aggregate State
    /// </summary>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public Task<AggregateState<TAggregatePayload>?> GetAggregateStateAsync<TAggregatePayload>() where TAggregatePayload : IAggregatePayloadCommon;
    /// <summary>
    ///     Get Aggregate State
    /// </summary>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public AggregateState<TAggregatePayload>? GetAggregateState<TAggregatePayload>() where TAggregatePayload : IAggregatePayloadCommon;
    /// <summary>
    ///     Check Aggregate type is specific type.
    /// </summary>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    public bool IsAggregateType<TAggregatePayload>() where TAggregatePayload : IAggregatePayloadCommon;
}
