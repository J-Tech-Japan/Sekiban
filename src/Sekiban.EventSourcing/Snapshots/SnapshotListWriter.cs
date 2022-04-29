namespace Sekiban.EventSourcing.Snapshots;

public class SnapshotListWriter
{
    // private readonly AggregateListService _aggregateListService;
    // private readonly IAggregateQueryStore _aggregateQueryStore;
    // private readonly IDocumentWriter _documentWriter;
    //
    // public SnapshotListWriter(
    //     AggregateListService aggregateListService,
    //     IDocumentWriter documentWriter,
    //     IAggregateQueryStore aggregateQueryStore)
    // {
    //     _aggregateListService = aggregateListService;
    //     _documentWriter = documentWriter;
    //     _aggregateQueryStore = aggregateQueryStore;
    // }
    //
    // public async Task TakeSnapshot<T, Q>(
    //     IPartitionKeyFactory partitionKeyFactory)
    //     where T : TransferableAggregateBase<Q>
    //     where Q : AggregateDtoBase, new()
    // {
    //     var list =
    //         await _aggregateListService.GetAggregateListObjectAsync<T, Q>();
    //     if (list.LastEventId == null) { return; }
    //     // take snapshot TODO make sure not taking duplicate shot
    //     var ids = new List<SnapshotListIndex>();
    //     foreach (var o in list.List)
    //     {
    //         var current = list.ProjectedSnapshot?.SnapshotIds.FirstOrDefault(
    //             m => m.AggregateId == o.AggregateId && m.Version == o.Version);
    //         if (current != null)
    //         {
    //             ids.Add(current);
    //         }
    //         else
    //         {
    //             var snapshot = new SnapshotDocument(
    //                 o.PartitionKeyFactory,
    //                 o.GetType().Name,
    //                 o.ToDto(),
    //                 o.AggregateId,
    //                 o.LastEventId);
    //             await _documentWriter.SaveAsync(snapshot);
    //             ids.Add(
    //                 new SnapshotListIndex(
    //                     snapshot.AggregateId,
    //                     snapshot.Id,
    //                     o.IsDeleted,
    //                     o.Version,
    //                     o.PartitionKeyFactory.GetPartitionKey(DocumentType.AggregateSnapshot)));
    //         }
    //     }
    //     var aggregateName = typeof(T).Name;
    //     var (snapshotList, snapshotListChunkDocuments) =
    //         SnapshotListDocument.CreateSnapshotListDocument(
    //             ids,
    //             null,
    //             list.LastEventId.Value,
    //             partitionKeyFactory,
    //             aggregateName);
    //     if (snapshotList != null)
    //     {
    //         await _documentWriter.SaveAsync(snapshotList);
    //     }
    //     if (snapshotListChunkDocuments != null)
    //     {
    //         foreach (var chunk in snapshotListChunkDocuments)
    //         {
    //             await _documentWriter.SaveAsync(chunk);
    //         }
    //     }
    //     list.ProjectedSnapshot = snapshotList;
    //     if (snapshotListChunkDocuments != null)
    //     {
    //         list.ProjectedSnapshotChunks.AddRange(snapshotListChunkDocuments);
    //     }
    //     // update memory list
    //     _aggregateQueryStore.SaveLatestAggregateList(list);
    // }
}
