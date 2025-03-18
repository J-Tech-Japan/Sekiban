using AspireEventSample.ApiService.Aggregates.ReadModel;
using AspireEventSample.ReadModels;
using Microsoft.EntityFrameworkCore;
namespace AspireEventSample.ApiService.Grains;

public class BranchEntityPostgresWriter : Grain, IBranchEntityPostgresWriter
{
    private readonly BranchDbContext _dbContext;

    public BranchEntityPostgresWriter(BranchDbContext dbContext) => _dbContext = dbContext;

    private static string GetCompositeKey(string rootPartitionKey, string aggregateGroup, Guid targetId) =>
        $"{rootPartitionKey}@{aggregateGroup}@{targetId}";

    public async Task<BranchEntity?> GetEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId)
    {
        var record = await _dbContext
            .Branches
            .Where(
                e => e.RootPartitionKey == rootPartitionKey &&
                    e.AggregateGroup == aggregateGroup &&
                    e.TargetId == targetId)
            .OrderByDescending(e => e.TimeStamp)
            .FirstOrDefaultAsync();
        if (record == null) return null;
        return BranchEntity.FromDbRecord(record);
    }

    public async Task<List<BranchEntity>> GetHistoryEntityByIdAsync(
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

        return records.Select(BranchEntity.FromDbRecord).ToList();
    }

    public async Task<BranchEntity> AddOrUpdateEntityAsync(BranchEntity entity)
    {
        // Convert to DB record
        var record = BranchEntity.ToDbRecord(entity);

        // Check if the entity already exists
        var existingEntity = await _dbContext.Branches.FindAsync(record.Id);

        if (existingEntity == null)
        {
            // Add new entity
            await _dbContext.Branches.AddAsync(record);
        } else
        {
            // Update existing entity
            _dbContext.Branches.Remove(existingEntity);
            await _dbContext.Branches.AddAsync(record);
        }

        await _dbContext.SaveChangesAsync();
        return entity;
    }
}
