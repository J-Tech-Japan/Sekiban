namespace AspireEventSample.ApiService.Grains;

using System.Collections.Concurrent;
using AspireEventSample.ApiService.Aggregates.ReadModel;
using Orleans;
using Sekiban.Pure.Documents;

public class BranchEntityWriter : Grain, IBranchEntityWriter
{
    private readonly ConcurrentDictionary<string, BranchEntity> _entities = new();

    private static string GetCompositeKey(string rootPartitionKey, string aggregateGroup, Guid targetId) =>
        $"{rootPartitionKey}@{aggregateGroup}@{targetId}";

    public Task<BranchEntity?> GetEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId)
    {
        var key = GetCompositeKey(rootPartitionKey, aggregateGroup, targetId);
        return Task.FromResult(_entities.TryGetValue(key, out var entity) ? entity : null);
    }

    public Task<List<BranchEntity>> GetHistoryEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId, string beforeSortableUniqueId)
    {
        // In a real implementation, this would query historical versions
        // For now, just return the current entity in a list if it exists
        var key = GetCompositeKey(rootPartitionKey, aggregateGroup, targetId);
        return Task.FromResult(_entities.TryGetValue(key, out var entity) 
            ? new List<BranchEntity> { entity } 
            : new List<BranchEntity>());
    }

    public Task<BranchEntity> AddOrUpdateEntityAsync(BranchEntity entity)
    {
        var key = GetCompositeKey(entity.RootPartitionKey, entity.AggregateGroup, entity.TargetId);
        _entities.AddOrUpdate(key, entity, (_, _) => entity);
        return Task.FromResult(entity);
    }
}
