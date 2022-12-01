using System.Collections.Immutable;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Shared;

namespace Sekiban.Core.Snapshot.Aggregate;

[AggregateContainerGroup(AggregateContainerGroup.InMemoryContainer)]
public record SnapshotManager
    (ImmutableList<string> Requests, ImmutableList<string> RequestTakens, DateTime CreatedAt) : IAggregatePayload
{
    internal const int SnapshotCount = 40;
    internal const int SnapshotTakeOffset = 15;

    public SnapshotManager() : this(ImmutableList<string>.Empty, ImmutableList<string>.Empty,
        SekibanDateProducer.GetRegistered().UtcNow)
    {
    }

    public static Guid SharedId { get; } = Guid.NewGuid();

    internal static string SnapshotKey(string aggregateTypeName, Guid targetAggregateId, int nextSnapshotVersion)
    {
        return $"{aggregateTypeName}_{targetAggregateId.ToString()}_{nextSnapshotVersion}";
    }
}
