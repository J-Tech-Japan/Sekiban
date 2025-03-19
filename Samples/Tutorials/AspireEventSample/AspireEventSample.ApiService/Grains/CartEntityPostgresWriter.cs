using AspireEventSample.ApiService.Aggregates.Carts;
using AspireEventSample.ReadModels;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
namespace AspireEventSample.ApiService.Grains;

public class CartEntityPostgresWriter : Grain, ICartEntityPostgresWriter
{
    private readonly BranchDbContext _dbContext;

    public CartEntityPostgresWriter(BranchDbContext dbContext) => _dbContext = dbContext;

    private static string GetCompositeKey(string rootPartitionKey, string aggregateGroup, Guid targetId) =>
        $"{rootPartitionKey}@{aggregateGroup}@{targetId}";

    public async Task<CartDbRecord?> GetEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId)
    {
        var record = await _dbContext
            .Carts
            .Where(
                e => e.RootPartitionKey == rootPartitionKey &&
                    e.AggregateGroup == aggregateGroup &&
                    e.TargetId == targetId)
            .OrderByDescending(e => e.TimeStamp)
            .FirstOrDefaultAsync();
        return record;
    }

    public async Task<List<CartDbRecord>> GetHistoryEntityByIdAsync(
        string rootPartitionKey,
        string aggregateGroup,
        Guid targetId,
        string beforeSortableUniqueId)
    {
        var records = await _dbContext
            .Carts
            .Where(
                e => e.RootPartitionKey == rootPartitionKey &&
                    e.AggregateGroup == aggregateGroup &&
                    e.TargetId == targetId &&
                    e.LastSortableUniqueId.CompareTo(beforeSortableUniqueId) < 0)
            .OrderByDescending(e => e.TimeStamp)
            .ToListAsync();

        return records;
    }

    public async Task<CartDbRecord> AddOrUpdateEntityAsync(CartDbRecord entity)
    {
        // Check if the entity already exists
        var existingEntity = await _dbContext.Carts.FindAsync(entity.Id);

        if (existingEntity == null)
        {
            // Add new entity
            await _dbContext.Carts.AddAsync(entity);
        } else
        {
            // Update existing entity
            _dbContext.Carts.Remove(existingEntity);
            await _dbContext.Carts.AddAsync(entity);
        }

        await _dbContext.SaveChangesAsync();
        return entity;
    }
}
