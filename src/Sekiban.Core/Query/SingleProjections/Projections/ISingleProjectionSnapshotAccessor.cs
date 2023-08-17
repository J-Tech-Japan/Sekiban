using Sekiban.Core.Aggregate;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Query.SingleProjections.Projections;

/// <summary>
///     Single projection snapshot accessor interface.
///     Developers does not need to implement this interface directly.
/// </summary>
public interface ISingleProjectionSnapshotAccessor
{
    /// <summary>
    ///     Create snapshot document from aggregate state.
    /// </summary>
    /// <param name="state"></param>
    /// <typeparam name="TPayload"></typeparam>
    /// <returns></returns>
    Task<SnapshotDocument?> SnapshotDocumentFromAggregateStateAsync<TPayload>(AggregateState<TPayload> state)
        where TPayload : IAggregatePayloadCommonBase;
    /// <summary>
    ///     Create Snapshot document from single projection state.
    /// </summary>
    /// <param name="state"></param>
    /// <param name="aggregateType"></param>
    /// <typeparam name="TPayload"></typeparam>
    /// <returns></returns>
    Task<SnapshotDocument?> SnapshotDocumentFromSingleProjectionStateAsync<TPayload>(SingleProjectionState<TPayload> state, Type aggregateType)
        where TPayload : class, ISingleProjectionPayloadCommon;
    /// <summary>
    ///     Fill snapshot document with blob.
    ///     When blob size is big, it will delegate to the blob storage.
    /// </summary>
    /// <param name="document"></param>
    /// <returns></returns>
    Task<SnapshotDocument?> FillSnapshotDocumentWithBlobAsync(SnapshotDocument document);
    /// <summary>
    ///     Fill snapshot document with JObject / blob.
    /// </summary>
    /// <param name="document"></param>
    /// <returns></returns>
    Task<SnapshotDocument?> FillSnapshotDocumentAsync(SnapshotDocument document);
    /// <summary>
    ///     Fill Snapshot document with JObject.
    /// </summary>
    /// <param name="document"></param>
    /// <returns></returns>
    Task<SnapshotDocument?> FillSnapshotDocumentWithJObjectAsync(SnapshotDocument document);
}
