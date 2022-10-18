using Sekiban.Core.Event;
namespace Sekiban.Core.Snapshot.Aggregate.Events;

public record SnapshotManagerRequestAdded(
    string AggregateTypeName,
    Guid TargetAggregateId,
    int NextSnapshotVersion,
    int? SnapshotVersion) : IChangedEventPayload;
