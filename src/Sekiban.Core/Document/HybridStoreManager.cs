using System.Collections.Concurrent;
using Xunit.Abstractions;

namespace Sekiban.Core.Document;

public class HybridStoreManager
{
    public HybridStoreManager(bool enabled)
    {
        Enabled = enabled;
    }

    private record HybridStatus(bool FromInitial, string SortableUniqueId);
    private ConcurrentDictionary<string, HybridStatus> HybridPartitionKeys { get; } = new();
    public ITestOutputHelper? TestOutputHelper { get; set; }

    public bool Enabled { get; set; }

    public bool HasPartition(string partitionKey)
    {
        return Enabled && HybridPartitionKeys.Keys.Contains(partitionKey);
    }

    public void ClearHybridPartitions()
    {
        HybridPartitionKeys.Clear();
    }

    public bool AddPartitionKey(string partitionKey, string sortableUniqueId, bool fromInitial)
    {
        if (!Enabled) return false;
        TestOutputHelper?.WriteLine($"adjusting partition key : {partitionKey} {fromInitial} {sortableUniqueId}");
        HybridPartitionKeys[partitionKey] = new HybridStatus(fromInitial,sortableUniqueId);
        return true;
    }

    public string? SortableUniqueIdForPartitionKey(string partitionKey)
    {
        if (!Enabled) return null;
        if (HybridPartitionKeys.Keys.Contains(partitionKey)) return HybridPartitionKeys[partitionKey].SortableUniqueId;
        return null;
    }
    public bool FromInitialForPartitionKey(string partitionKey)
    {
        if (!Enabled) return false;
        if (HybridPartitionKeys.Keys.Contains(partitionKey)) return HybridPartitionKeys[partitionKey].FromInitial;
        return false;
    }
}
