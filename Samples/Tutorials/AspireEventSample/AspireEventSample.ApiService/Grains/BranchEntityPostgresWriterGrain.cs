using AspireEventSample.ReadModels;
using Microsoft.Extensions.Logging;

namespace AspireEventSample.ApiService.Grains;

public class BranchEntityPostgresWriterGrain : Grain, IBranchEntityPostgresWriterGrain
{
    private readonly IBranchWriter _branchWriter;
    private readonly ILogger<BranchEntityPostgresWriterGrain> _logger;

    public BranchEntityPostgresWriterGrain(
        BranchEntityPostgresWriter branchWriter,
        ILogger<BranchEntityPostgresWriterGrain> logger)
    {
        _branchWriter = branchWriter;
        _logger = logger;
    }

    public Task<BranchDbRecord?> GetEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId)
    {
        _logger.LogDebug("Getting branch entity with ID {BranchId}", targetId);
        return _branchWriter.GetEntityByIdAsync(rootPartitionKey, aggregateGroup, targetId);
    }

    public Task<List<BranchDbRecord>> GetHistoryEntityByIdAsync(
        string rootPartitionKey,
        string aggregateGroup,
        Guid targetId,
        string beforeSortableUniqueId)
    {
        _logger.LogDebug("Getting branch entity history with ID {BranchId}", targetId);
        return _branchWriter.GetHistoryEntityByIdAsync(rootPartitionKey, aggregateGroup, targetId, beforeSortableUniqueId);
    }

    public Task<BranchDbRecord> AddOrUpdateEntityAsync(BranchDbRecord entity)
    {
        _logger.LogDebug("Adding or updating branch entity with ID {BranchId}", entity.TargetId);
        return _branchWriter.AddOrUpdateEntityAsync(entity);
    }
    
    public Task<string> GetLastSortableUniqueIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId)
    {
        _logger.LogDebug("Getting last sortable unique ID for branch with ID {BranchId}", targetId);
        return _branchWriter.GetLastSortableUniqueIdAsync(rootPartitionKey, aggregateGroup, targetId);
    }
}
