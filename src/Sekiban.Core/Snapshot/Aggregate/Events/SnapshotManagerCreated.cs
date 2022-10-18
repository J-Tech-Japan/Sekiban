using Sekiban.Core.Event;
namespace Sekiban.Core.Snapshot.Aggregate.Events;

public record SnapshotManagerCreated(DateTime CreatedAt) : ICreatedEventPayload;
