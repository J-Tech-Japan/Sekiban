using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Storage;

/// <summary>
///     A single-event conditional-append request: append <see cref="Event" /> only if <see cref="IdempotencyKey" /> has
///     not already been claimed by a DIFFERENT operation. The store derives the canonical operation fingerprint from the
///     key + the event content (never from the server-generated <see cref="SerializableEvent.Id" /> /
///     <see cref="SerializableEvent.SortableUniqueIdValue" />, which differ on every retry), so a genuine retry of the same
///     operation is recognised as <see cref="ConditionalAppendStatus.AlreadyCommittedSameOperation" /> even though it
///     carries freshly-allocated ids.
///     ServiceId is NOT part of this DTO: it is a store-layer tenant identity resolved via
///     <c>IServiceIdProvider</c> and folded into the fingerprint by the store, so no cross-service claim is possible.
/// </summary>
public sealed record ConditionalAppendRequest(
    string IdempotencyKey,
    SerializableEvent Event);
