using System.Collections.Concurrent;
namespace Sekiban.EventSourcing.Documents;

public class HybridStoreManager
{
    private BlockingCollection<string> HybridPartitionKeys { get; init; } = new();
    public bool Enabled { get; set; } 
    public HybridStoreManager(bool elanbled) => Enabled = elanbled;
    public bool HasPartition(string partitionKey)
    {
        return Enabled ? HybridPartitionKeys.Contains(partitionKey) : false;
    }
    public void AddPartitionKey(string partitionKey)
    {
        if (!Enabled ) return;
        HybridPartitionKeys.Add(partitionKey);
    }
}
