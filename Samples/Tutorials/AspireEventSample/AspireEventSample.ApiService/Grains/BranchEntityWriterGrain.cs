namespace AspireEventSample.ApiService.Grains;

using System.Collections.Concurrent;
using AspireEventSample.ReadModels;
using Orleans;
using Sekiban.Pure.Documents;

public class BranchEntityWriterGrain : Grain, IBranchEntityWriterGrain
{
    private readonly ConcurrentDictionary<string, BranchDbRecord> _entities = new();

    private static string GetCompositeKey(string rootPartitionKey, string aggregateGroup, Guid targetId) =>
        $"{rootPartitionKey}@{aggregateGroup}@{targetId}";

    public Task<BranchDbRecord?> GetEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId)
    {
        var key = GetCompositeKey(rootPartitionKey, aggregateGroup, targetId);
        return Task.FromResult(_entities.TryGetValue(key, out var entity) ? entity : null);
    }

    public Task<List<BranchDbRecord>> GetHistoryEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId, string beforeSortableUniqueId)
    {
        // In a real implementation, this would query historical versions
        // For now, just return the current entity in a list if it exists
        var key = GetCompositeKey(rootPartitionKey, aggregateGroup, targetId);
        return Task.FromResult(_entities.TryGetValue(key, out var entity) 
            ? new List<BranchDbRecord> { entity } 
            : new List<BranchDbRecord>());
    }

    public Task<BranchDbRecord> AddOrUpdateEntityAsync(BranchDbRecord entity)
    {
        var key = GetCompositeKey(entity.RootPartitionKey, entity.AggregateGroup, entity.TargetId);
        _entities.AddOrUpdate(key, entity, (_, _) => entity);
        return Task.FromResult(entity);
    }
}
