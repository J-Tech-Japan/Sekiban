using System.Collections.Concurrent;
namespace Sekiban.Core.Documents;

/// <summary>
///     System use keep in memory event caches.
///     Application developers does not need to use this interface directly
/// </summary>
public class HybridStoreManager
{
    private ConcurrentDictionary<string, HybridStatus> HybridPartitionKeys { get; } = new();
    public bool Enabled { get; set; }
    public HybridStoreManager(bool enabled) => Enabled = enabled;

    public bool HasPartition(string partitionKey) => Enabled && HybridPartitionKeys.ContainsKey(partitionKey);

    public void ClearHybridPartitions()
    {
        HybridPartitionKeys.Clear();
    }

    public bool AddPartitionKey(string partitionKey, string sortableUniqueId, bool fromInitial)
    {
        if (!Enabled)
        {
            return false;
        }
        HybridPartitionKeys[partitionKey] = new HybridStatus(fromInitial, sortableUniqueId);
        return true;
    }

    public string? SortableUniqueIdForPartitionKey(string partitionKey)
    {
        return Enabled && HybridPartitionKeys.TryGetValue(partitionKey, out var value)
            ? value.SortableUniqueId
            : null;
    }

    public bool FromInitialForPartitionKey(string partitionKey)
    {
        return Enabled && HybridPartitionKeys.TryGetValue(partitionKey, out var value) && value.FromInitial;
    }

    private record HybridStatus(bool FromInitial, string SortableUniqueId);
}
