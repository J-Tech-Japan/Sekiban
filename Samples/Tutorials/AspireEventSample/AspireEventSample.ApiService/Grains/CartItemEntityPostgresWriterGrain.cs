using AspireEventSample.ReadModels;
using Microsoft.EntityFrameworkCore;
namespace AspireEventSample.ApiService.Grains;

public class CartItemEntityPostgresWriterGrain : Grain, ICartItemEntityPostgresWriterGrain
{
    private readonly BranchDbContext _dbContext;
    private readonly ILogger<CartItemEntityPostgresWriterGrain> _logger;

    public CartItemEntityPostgresWriterGrain(
        BranchDbContext dbContext,
        ILogger<CartItemEntityPostgresWriterGrain> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CartItemDbRecord> AddOrUpdateEntityAsync(CartItemDbRecord entity)
    {
        try
        {
            var existingEntity = await _dbContext.CartItems.FindAsync(entity.Id);
            if (existingEntity == null)
            {
                await _dbContext.CartItems.AddAsync(entity);
            } else
            {
                _dbContext.Entry(existingEntity).CurrentValues.SetValues(entity);
            }

            await _dbContext.SaveChangesAsync();
            return entity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding or updating cart item entity");
            throw;
        }
    }

    public async Task<CartItemDbRecord?> GetEntityByIdAsync(
        string rootPartitionKey,
        string aggregateGroup,
        Guid targetId)
    {
        try
        {
            return await _dbContext
                .CartItems
                .Where(
                    e => e.RootPartitionKey == rootPartitionKey &&
                        e.AggregateGroup == aggregateGroup &&
                        e.TargetId == targetId)
                .OrderByDescending(e => e.LastSortableUniqueId)
                .FirstOrDefaultAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cart item entity by ID");
            throw;
        }
    }

    public async Task<List<CartItemDbRecord>> GetHistoryEntityByIdAsync(
        string rootPartitionKey,
        string aggregateGroup,
        Guid targetId,
        string beforeSortableUniqueId)
    {
        try
        {
            return await _dbContext
                .CartItems
                .Where(
                    e => e.RootPartitionKey == rootPartitionKey &&
                        e.AggregateGroup == aggregateGroup &&
                        e.TargetId == targetId &&
                        string.Compare(e.LastSortableUniqueId, beforeSortableUniqueId) <= 0)
                .OrderByDescending(e => e.LastSortableUniqueId)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cart item entity history");
            throw;
        }
    }

    public async Task<string> GetLastSortableUniqueIdAsync(
        string rootPartitionKey,
        string aggregateGroup,
        Guid targetId)
    {
        try
        {
            var entity = await _dbContext
                .CartItems
                .Where(
                    e => e.RootPartitionKey == rootPartitionKey &&
                        e.AggregateGroup == aggregateGroup &&
                        e.TargetId == targetId)
                .OrderByDescending(e => e.LastSortableUniqueId)
                .FirstOrDefaultAsync();

            return entity?.LastSortableUniqueId ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting last sortable unique ID for cart item entity");
            throw;
        }
    }

    public async Task<List<CartItemDbRecord>> GetItemsByCartIdAsync(Guid cartId)
    {
        try
        {
            return await _dbContext
                .CartItems
                .Where(e => e.CartId == cartId)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cart items by cart ID");
            throw;
        }
    }
}