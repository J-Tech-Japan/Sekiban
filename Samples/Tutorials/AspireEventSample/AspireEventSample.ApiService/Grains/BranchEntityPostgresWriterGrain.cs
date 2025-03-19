using AspireEventSample.ReadModels;
using Microsoft.EntityFrameworkCore;
namespace AspireEventSample.ApiService.Grains;

public class BranchEntityPostgresWriterGrain : Grain, IBranchEntityPostgresWriterGrain
{
    private readonly BranchDbContext _dbContext;

    public BranchEntityPostgresWriterGrain(BranchDbContext dbContext) => _dbContext = dbContext;

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
        // Check if the entity already exists
        var existingEntity = await _dbContext.Branches.FindAsync(entity.Id);

        if (existingEntity == null)
        {
            // Add new entity
            await _dbContext.Branches.AddAsync(entity);
        } else
        {
            // Update existing entity
            _dbContext.Branches.Remove(existingEntity);
            await _dbContext.Branches.AddAsync(entity);
        }

        await _dbContext.SaveChangesAsync();
        return entity;
    }
}
