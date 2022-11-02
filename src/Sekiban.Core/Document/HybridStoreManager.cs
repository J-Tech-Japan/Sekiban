using System.Collections.Concurrent;
namespace Sekiban.Core.Document;

public class HybridStoreManager
{
    public HybridStoreManager(bool elanbled) => Enabled = elanbled;
    private ConcurrentDictionary<string, string> HybridPartitionKeys
    {
        get;
    } = new();
    public bool Enabled { get; set; }
    public bool HasPartition(string partitionKey) => Enabled && HybridPartitionKeys.Keys.Contains(partitionKey);
    public void ClearHybridPartitions()
    {
        HybridPartitionKeys.Clear();
    }
    public bool AddPartitionKey(string partitionKey, string sortableUniqueId)
    {
        if (!Enabled)
        {
            return false;
        }
        HybridPartitionKeys[partitionKey] = sortableUniqueId;
        return true;
    }
    public string? SortableUniqueIdForPartitionKey(string partitionKey)
    {
        if (!Enabled)
        {
            return null;
        }
        if (HybridPartitionKeys.Keys.Contains(partitionKey))
        {
            return HybridPartitionKeys[partitionKey];
        }
        return null;
    }
}
