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

    public bool HasPartition(string partitionKey) => Enabled && HybridPartitionKeys.Keys.Contains(partitionKey);

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
        if (!Enabled)
        {
            return null;
        }
        return HybridPartitionKeys.Keys.Contains(partitionKey) ? HybridPartitionKeys[partitionKey].SortableUniqueId : null;
    }
    public bool FromInitialForPartitionKey(string partitionKey)
    {
        if (!Enabled)
        {
            return false;
        }
        return HybridPartitionKeys.Keys.Contains(partitionKey) ? HybridPartitionKeys[partitionKey].FromInitial : false;
    }

    private record HybridStatus(bool FromInitial, string SortableUniqueId);
}
