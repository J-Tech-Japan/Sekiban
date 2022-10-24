using Sekiban.Core.Aggregate;
using Sekiban.Core.Shared;
using System.Collections.Immutable;
namespace Sekiban.Core.Snapshot.Aggregate;

[AggregateContainerGroup(AggregateContainerGroup.InMemoryContainer)]
public record SnapshotManager(ImmutableList<string> Requests,ImmutableList<string> RequestTakens,DateTime CreatedAt ) : IAggregatePayload
{
    internal const int SnapshotCount = 40;
    internal const int SnapshotTakeOffset = 15;
    public static Guid SharedId { get; } = Guid.NewGuid();

    public SnapshotManager() : this(ImmutableList<string>.Empty, ImmutableList<string>.Empty, SekibanDateProducer.GetRegistered().UtcNow)
    {
    }
    internal static string SnapshotKey(string aggregateTypeName, Guid targetAggregateId, int nextSnapshotVersion)
    {
        return $"{aggregateTypeName}_{targetAggregateId.ToString()}_{nextSnapshotVersion}";
    }

}
