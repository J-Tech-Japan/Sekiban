using System.Collections.Concurrent;
namespace Sekiban.EventSourcing.Documents;

public class HybridStoreManager
{
    public BlockingCollection<string> HybridPartitionKeys { get; init; } =
        new BlockingCollection<string>();
}
