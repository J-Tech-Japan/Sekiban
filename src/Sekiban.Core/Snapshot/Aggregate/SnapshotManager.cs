using Sekiban.Core.Aggregate;
using Sekiban.Core.Shared;
using System.Collections.Immutable;
namespace Sekiban.Core.Snapshot.Aggregate;

/// <summary>
///     Snapshot Manager Aggregate. This class is internal use for the sekiban.
///     Snapshot Manager Aggregate manages current snapshot versions in memory.
/// </summary>
/// <param name="Requests"></param>
/// <param name="RequestTakens"></param>
/// <param name="CreatedAt"></param>
[AggregateContainerGroup(AggregateContainerGroup.InMemory)]
public record SnapshotManager
    (ImmutableList<string> Requests, ImmutableList<string> RequestTakens, DateTime CreatedAt) : IAggregatePayload<SnapshotManager>
{
    internal const int SnapshotCount = 40;
    internal const int SnapshotTakeOffset = 15;

    public static Guid SharedId { get; } = Guid.NewGuid();

    public SnapshotManager() : this(ImmutableList<string>.Empty, ImmutableList<string>.Empty, SekibanDateProducer.GetRegistered().UtcNow)
    {
    }
    public static SnapshotManager CreateInitialPayload(SnapshotManager? _) =>
        new(ImmutableList<string>.Empty, ImmutableList<string>.Empty, SekibanDateProducer.GetRegistered().UtcNow);
    internal static string SnapshotKey(string aggregateTypeName, Guid targetAggregateId, int nextSnapshotVersion) =>
        $"{aggregateTypeName}_{targetAggregateId.ToString()}_{nextSnapshotVersion}";
}
