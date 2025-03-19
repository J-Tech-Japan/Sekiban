using AspireEventSample.ReadModels;
namespace AspireEventSample.ApiService.Grains;

public class BranchEntityPostgresWriterGrain : Grain, IBranchEntityPostgresWriterGrain
{
    private readonly IBranchWriter _branchWriter;

    public BranchEntityPostgresWriterGrain(BranchEntityPostgresWriter branchWriter) => _branchWriter = branchWriter;

    public Task<BranchDbRecord?> GetEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId)
    {
        return _branchWriter.GetEntityByIdAsync(rootPartitionKey, aggregateGroup, targetId);
    }

    public Task<List<BranchDbRecord>> GetHistoryEntityByIdAsync(
        string rootPartitionKey,
        string aggregateGroup,
        Guid targetId,
        string beforeSortableUniqueId)
    {
        return _branchWriter.GetHistoryEntityByIdAsync(rootPartitionKey, aggregateGroup, targetId, beforeSortableUniqueId);
    }

    public Task<BranchDbRecord> AddOrUpdateEntityAsync(BranchDbRecord entity)
    {
        return _branchWriter.AddOrUpdateEntityAsync(entity);
    }
}
