using AspireEventSample.ReadModels;
using Microsoft.EntityFrameworkCore;
namespace AspireEventSample.ApiService.Grains;

public class BranchPostgresReadModelAccessor : IBranchReadModelAccessor
{
    private readonly BranchDbContext _dbContext;
    private readonly ILogger<BranchPostgresReadModelAccessor> _logger;

    public BranchPostgresReadModelAccessor(
        BranchDbContext dbContext,
        ILogger<BranchPostgresReadModelAccessor> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    private static string GetCompositeKey(string rootPartitionKey, string aggregateGroup, Guid targetId) =>
        $"{rootPartitionKey}@{aggregateGroup}@{targetId}";

    public async Task<BranchDbRecord?> GetEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId)
    {
        var record = await _dbContext
            .Branches
            .Where(
                e => e.RootPartitionKey == rootPartitionKey &&
                    e.AggregateGroup == aggregateGroup &&
                    e.TargetId == targetId)
            .OrderByDescending(e => e.TimeStamp)
            .FirstOrDefaultAsync();
        return record;
    }

    public async Task<List<BranchDbRecord>> GetHistoryEntityByIdAsync(
        string rootPartitionKey,
        string aggregateGroup,
        Guid targetId,
        string beforeSortableUniqueId)
    {
        var records = await _dbContext
            .Branches
            .Where(
                e => e.RootPartitionKey == rootPartitionKey &&
                    e.AggregateGroup == aggregateGroup &&
                    e.TargetId == targetId &&
                    e.LastSortableUniqueId.CompareTo(beforeSortableUniqueId) < 0)
            .OrderByDescending(e => e.TimeStamp)
            .ToListAsync();

        return records;
    }

    public async Task<BranchDbRecord> AddOrUpdateEntityAsync(BranchDbRecord entity)
    {
        try
        {
            // Check if the entity already exists
            var existingEntity = await _dbContext.Branches.FindAsync(entity.Id);

            if (existingEntity == null)
            {
                // Add new entity
                _logger.LogDebug(
                    "Adding new branch entity with ID {BranchId}, name: {BranchName}",
                    entity.TargetId,
                    entity.Name);

                await _dbContext.Branches.AddAsync(entity);
            } else
            {
                // Update existing entity
                _logger.LogDebug(
                    "Updating branch entity with ID {BranchId}, name: {BranchName}",
                    entity.TargetId,
                    entity.Name);

                _dbContext.Branches.Remove(existingEntity);
                await _dbContext.Branches.AddAsync(entity);
            }

            await _dbContext.SaveChangesAsync();
            return entity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving branch entity with ID {BranchId}", entity.TargetId);
            throw;
        }
    }

    public async Task<string> GetLastSortableUniqueIdAsync(
        string rootPartitionKey,
        string aggregateGroup,
        Guid targetId)
    {
        var record = await _dbContext
            .Branches
            .Where(
                e => e.RootPartitionKey == rootPartitionKey &&
                    e.AggregateGroup == aggregateGroup &&
                    e.TargetId == targetId)
            .OrderByDescending(e => e.TimeStamp)
            .FirstOrDefaultAsync();

        return record?.LastSortableUniqueId ?? string.Empty;
    }
}