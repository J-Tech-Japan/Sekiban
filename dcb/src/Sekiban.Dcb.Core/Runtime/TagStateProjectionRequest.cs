using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Runtime;

/// <summary>
///     Serialized request contract for tag state projection.
/// </summary>
public sealed record TagStateProjectionRequest(
    TagStateId TagStateId,
    string? LatestSortableUniqueId,
    SerializableTagState? CachedState,
    IReadOnlyList<SerializableEvent> Events);
