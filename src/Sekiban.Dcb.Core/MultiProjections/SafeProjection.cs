namespace Sekiban.Dcb.MultiProjections;

public readonly record struct SafeProjection<T>(T State, string SafeLastSortableUniqueId, int Version) where T : IMultiProjectionPayload;