using System.Collections.Concurrent;
using AspireEventSample.ApiService.Aggregates.ReadModel;
using AspireEventSample.ReadModels;
using Microsoft.Extensions.Logging;
using Orleans;

namespace AspireEventSample.ApiService.Grains;

public class BranchEntityWriterGrain : Grain, IBranchEntityWriterGrain
{
    private readonly ConcurrentDictionary<string, BranchDbRecord> _entities = new();
    private readonly ILogger<BranchEntityWriterGrain> _logger;

    public BranchEntityWriterGrain(ILogger<BranchEntityWriterGrain> logger)
    {
        _logger = logger;
    }

    private static string GetCompositeKey(string rootPartitionKey, string aggregateGroup, Guid targetId) =>
        $"{rootPartitionKey}@{aggregateGroup}@{targetId}";

    public Task<BranchDbRecord?> GetEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId)
    {
        var key = GetCompositeKey(rootPartitionKey, aggregateGroup, targetId);
        _logger.LogDebug("Getting branch entity with ID {BranchId}", targetId);
        return Task.FromResult(_entities.TryGetValue(key, out var entity) ? entity : null);
    }

    public Task<List<BranchDbRecord>> GetHistoryEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId, string beforeSortableUniqueId)
    {
        // In a real implementation, this would query historical versions
        // For now, just return the current entity in a list if it exists
        var key = GetCompositeKey(rootPartitionKey, aggregateGroup, targetId);
        _logger.LogDebug("Getting branch entity history with ID {BranchId}", targetId);
        return Task.FromResult(_entities.TryGetValue(key, out var entity) 
            ? new List<BranchDbRecord> { entity } 
            : new List<BranchDbRecord>());
    }

    public Task<BranchDbRecord> AddOrUpdateEntityAsync(BranchDbRecord entity)
    {
        var key = GetCompositeKey(entity.RootPartitionKey, entity.AggregateGroup, entity.TargetId);
        _logger.LogDebug("Adding or updating branch entity with ID {BranchId}", entity.TargetId);
        _entities.AddOrUpdate(key, entity, (_, _) => entity);
        return Task.FromResult(entity);
    }
    
    public Task<string> GetLastSortableUniqueIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId)
    {
        var key = GetCompositeKey(rootPartitionKey, aggregateGroup, targetId);
        _logger.LogDebug("Getting last sortable unique ID for branch with ID {BranchId}", targetId);
        return Task.FromResult(_entities.TryGetValue(key, out var entity) ? entity.LastSortableUniqueId : string.Empty);
    }
}
