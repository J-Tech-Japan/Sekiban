using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Documents;

/// <summary>
///     Document Persistent Repository, it is to save database like cosmos or dynamo
/// </summary>
public interface IDocumentPersistentRepository : IDocumentRepository
{
    /// <summary>
    ///     Get Snapshots saved to the document store.
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="aggregatePayloadType"></param>
    /// <param name="projectionPayloadType"></param>
    /// <param name="rootPartitionKey"></param>
    /// <returns></returns>
    Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey);
}
