using System.Collections.Concurrent;
using AspireEventSample.ApiService.Aggregates.ReadModel;
using Microsoft.Extensions.Logging;
using Orleans;

namespace AspireEventSample.ApiService.Grains;

public class CartEntityWriter : Grain, ICartEntityWriter
{
    private readonly ConcurrentDictionary<string, CartEntity> _entities = new();
    private readonly ILogger<CartEntityWriter> _logger;

    public CartEntityWriter(ILogger<CartEntityWriter> logger)
    {
        _logger = logger;
    }

    private static string GetCompositeKey(string rootPartitionKey, string aggregateGroup, Guid targetId) =>
        $"{rootPartitionKey}@{aggregateGroup}@{targetId}";

    public Task<CartEntity?> GetEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId)
    {
        var key = GetCompositeKey(rootPartitionKey, aggregateGroup, targetId);
        _logger.LogDebug("Getting cart entity with ID {CartId}", targetId);
        return Task.FromResult(_entities.TryGetValue(key, out var entity) ? entity : null);
    }

    public Task<List<CartEntity>> GetHistoryEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId, string beforeSortableUniqueId)
    {
        // In a real implementation, this would query historical versions
        // For now, just return the current entity in a list if it exists
        var key = GetCompositeKey(rootPartitionKey, aggregateGroup, targetId);
        _logger.LogDebug("Getting cart entity history with ID {CartId}", targetId);
        return Task.FromResult(_entities.TryGetValue(key, out var entity) 
            ? new List<CartEntity> { entity } 
            : new List<CartEntity>());
    }

    public Task<CartEntity> AddOrUpdateEntityAsync(CartEntity entity)
    {
        var key = GetCompositeKey(entity.RootPartitionKey, entity.AggregateGroup, entity.TargetId);
        _logger.LogDebug("Adding or updating cart entity with ID {CartId}", entity.TargetId);
        _entities.AddOrUpdate(key, entity, (_, _) => entity);
        return Task.FromResult(entity);
    }
    
    public Task<string> GetLastSortableUniqueIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId)
    {
        var key = GetCompositeKey(rootPartitionKey, aggregateGroup, targetId);
        _logger.LogDebug("Getting last sortable unique ID for cart with ID {CartId}", targetId);
        return Task.FromResult(_entities.TryGetValue(key, out var entity) ? entity.LastSortableUniqueId : string.Empty);
    }
}
