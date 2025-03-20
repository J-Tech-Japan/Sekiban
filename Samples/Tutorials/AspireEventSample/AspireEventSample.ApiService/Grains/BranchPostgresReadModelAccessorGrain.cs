using AspireEventSample.ReadModels;
namespace AspireEventSample.ApiService.Grains;

public class BranchPostgresReadModelAccessorGrain : Grain, IBranchEntityPostgresReadModelAccessorGrain
{
    private readonly IBranchReadModelAccessor _branchReadModelAccessor;
    private readonly ILogger<BranchPostgresReadModelAccessorGrain> _logger;

    public BranchPostgresReadModelAccessorGrain(
        BranchPostgresReadModelAccessor branchReadModelAccessor,
        ILogger<BranchPostgresReadModelAccessorGrain> logger)
    {
        _branchReadModelAccessor = branchReadModelAccessor;
        _logger = logger;
    }

    public Task<BranchDbRecord?> GetEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId)
    {
        _logger.LogDebug("Getting branch entity with ID {BranchId}", targetId);
        return _branchReadModelAccessor.GetEntityByIdAsync(rootPartitionKey, aggregateGroup, targetId);
    }

    public Task<List<BranchDbRecord>> GetHistoryEntityByIdAsync(
        string rootPartitionKey,
        string aggregateGroup,
        Guid targetId,
        string beforeSortableUniqueId)
    {
        _logger.LogDebug("Getting branch entity history with ID {BranchId}", targetId);
        return _branchReadModelAccessor.GetHistoryEntityByIdAsync(
            rootPartitionKey,
            aggregateGroup,
            targetId,
            beforeSortableUniqueId);
    }

    public Task<BranchDbRecord> AddOrUpdateEntityAsync(BranchDbRecord entity)
    {
        _logger.LogDebug("Adding or updating branch entity with ID {BranchId}", entity.TargetId);
        return _branchReadModelAccessor.AddOrUpdateEntityAsync(entity);
    }

    public Task<string> GetLastSortableUniqueIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId)
    {
        _logger.LogDebug("Getting last sortable unique ID for branch with ID {BranchId}", targetId);
        return _branchReadModelAccessor.GetLastSortableUniqueIdAsync(rootPartitionKey, aggregateGroup, targetId);
    }
}