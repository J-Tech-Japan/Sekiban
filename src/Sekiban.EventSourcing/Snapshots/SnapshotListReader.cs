namespace Sekiban.EventSourcing.Snapshots;

public class SnapshotListReader
{
    private readonly IDocumentRepository _documentRepository;

    public SnapshotListReader(IDocumentRepository documentRepository) =>
        _documentRepository = documentRepository;

    public async Task<SingleAggregateList<T>?> GetAggregateListFromSnapshotListAsync<T, Q>()
        where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase, new()
    {
        var snapshotList = await _documentRepository.GetLatestSnapshotListForTypeAsync<T>(null);
        if (snapshotList == default) { return default; }

        var aggregateList = new SingleAggregateList<T>();
        aggregateList.ProjectedSnapshot = snapshotList;
        foreach (var chunk in snapshotList.SnapshotListChunkIds)
        {
            var snapshotListChunk = await _documentRepository.GetSnapshotListChunkByIdAsync(chunk, snapshotList.PartitionKey);
            if (snapshotListChunk == null)
            {
                continue;
            }
            aggregateList.ProjectedSnapshotChunks.Add(snapshotListChunk);
        }
        foreach (var i in aggregateList.MergedSnapshotIds)
        {
            var snapshot = await _documentRepository.GetSnapshotByIdAsync(i.SnapshotId, typeof(T), i.PartitionKey);
            if (snapshot == default)
            {
                continue;
            }
            var dto = snapshot.ToDto<Q>();
            if (dto == default)
            {
                continue;
            }
            var aggregate = AggregateBase.Create<T>(snapshot.AggregateId);
            aggregate.ApplySnapshot(dto);
            aggregateList.List.Add(aggregate);
        }
        aggregateList.LastEventId = snapshotList.LastEventId;
        return aggregateList;
    }
}
