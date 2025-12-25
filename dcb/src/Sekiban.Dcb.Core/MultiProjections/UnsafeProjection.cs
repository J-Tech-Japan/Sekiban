namespace Sekiban.Dcb.MultiProjections;

public readonly record struct UnsafeProjection<T>(T State, string LastSortableUniqueId, Guid LastEventId, int Version) where T : IMultiProjectionPayload;