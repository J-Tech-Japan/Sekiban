using AspireEventSample.ApiService.Aggregates.Carts;
using AspireEventSample.ReadModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AspireEventSample.ApiService.Grains;

public class CartEntityPostgresWriter : Grain, ICartEntityPostgresWriter
{
    private readonly BranchDbContext _dbContext;
    private readonly ILogger<CartEntityPostgresWriter> _logger;

    public CartEntityPostgresWriter(
        BranchDbContext dbContext,
        ILogger<CartEntityPostgresWriter> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

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
        try
        {
            // Check if the entity already exists
            var existingEntity = await _dbContext.Carts.FindAsync(entity.Id);

            if (existingEntity == null)
            {
                // Add new entity
                _logger.LogDebug("Adding new cart entity with ID {CartId}", entity.TargetId);
                await _dbContext.Carts.AddAsync(entity);
            }
            else
            {
                // Update existing entity
                _logger.LogDebug("Updating cart entity with ID {CartId}", entity.TargetId);
                _dbContext.Carts.Remove(existingEntity);
                await _dbContext.Carts.AddAsync(entity);
            }

            await _dbContext.SaveChangesAsync();
            return entity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving cart entity with ID {CartId}", entity.TargetId);
            throw;
        }
    }
    
    public async Task<string> GetLastSortableUniqueIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId)
    {
        _logger.LogDebug("Getting last sortable unique ID for cart with ID {CartId}", targetId);
        var record = await _dbContext
            .Carts
            .Where(
                e => e.RootPartitionKey == rootPartitionKey &&
                    e.AggregateGroup == aggregateGroup &&
                    e.TargetId == targetId)
            .OrderByDescending(e => e.TimeStamp)
            .FirstOrDefaultAsync();
            
        return record?.LastSortableUniqueId ?? string.Empty;
    }
}
