using System;
using System.Collections.Generic;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     WithoutResult variant of <see cref="GenericTagMultiProjector{TTagProjector, TTagGroup}"/>.
///     Delegates projection/custom-serialization to the existing projector while exposing
///     exception-based flow and safe/unsafe accessors.
/// </summary>
public record GenericTagMultiProjectorWithoutResult<TTagProjector, TTagGroup> :
    IMultiProjectorWithCustomSerializationWithoutResult<GenericTagMultiProjectorWithoutResult<TTagProjector, TTagGroup>>,
    ISafeAndUnsafeStateAccessor<GenericTagMultiProjectorWithoutResult<TTagProjector, TTagGroup>>
    where TTagProjector : ITagProjector<TTagProjector>
    where TTagGroup : IGuidTagGroup<TTagGroup>
{
    public GenericTagMultiProjector<TTagProjector, TTagGroup> Inner { get; init; } = new();

    public static string MultiProjectorName => GenericTagMultiProjector<TTagProjector, TTagGroup>.MultiProjectorName;

    public static string MultiProjectorVersion => GenericTagMultiProjector<TTagProjector, TTagGroup>.MultiProjectorVersion;

    public static GenericTagMultiProjectorWithoutResult<TTagProjector, TTagGroup> GenerateInitialPayload() => new();

    public static GenericTagMultiProjectorWithoutResult<TTagProjector, TTagGroup> Project(
        GenericTagMultiProjectorWithoutResult<TTagProjector, TTagGroup> payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        var result = GenericTagMultiProjector<TTagProjector, TTagGroup>
            .Project(payload.Inner, ev, tags, domainTypes, safeWindowThreshold)
            .UnwrapBox();

        return payload with { Inner = result };
    }

    public static byte[] Serialize(
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        GenericTagMultiProjectorWithoutResult<TTagProjector, TTagGroup> payload) =>
        GenericTagMultiProjector<TTagProjector, TTagGroup>.Serialize(domainTypes, safeWindowThreshold, payload.Inner);

    public static GenericTagMultiProjectorWithoutResult<TTagProjector, TTagGroup> Deserialize(
        DcbDomainTypes domainTypes,
        ReadOnlySpan<byte> data) =>
        new() { Inner = GenericTagMultiProjector<TTagProjector, TTagGroup>.Deserialize(domainTypes, data) };

    public SafeProjection<GenericTagMultiProjectorWithoutResult<TTagProjector, TTagGroup>> GetSafeProjection(
        SortableUniqueId safeWindowThreshold,
        DcbDomainTypes domainTypes)
    {
        var innerSafe = ((ISafeAndUnsafeStateAccessor<GenericTagMultiProjector<TTagProjector, TTagGroup>>)Inner)
            .GetSafeProjection(safeWindowThreshold, domainTypes);
        return new SafeProjection<GenericTagMultiProjectorWithoutResult<TTagProjector, TTagGroup>>(
            this,
            innerSafe.SafeLastSortableUniqueId,
            innerSafe.Version);
    }

    public UnsafeProjection<GenericTagMultiProjectorWithoutResult<TTagProjector, TTagGroup>> GetUnsafeProjection(
        DcbDomainTypes domainTypes)
    {
        var innerUnsafe = ((ISafeAndUnsafeStateAccessor<GenericTagMultiProjector<TTagProjector, TTagGroup>>)Inner)
            .GetUnsafeProjection(domainTypes);
        return new UnsafeProjection<GenericTagMultiProjectorWithoutResult<TTagProjector, TTagGroup>>(
            this,
            innerUnsafe.LastSortableUniqueId,
            innerUnsafe.LastEventId,
            innerUnsafe.Version);
    }

    public ISafeAndUnsafeStateAccessor<GenericTagMultiProjectorWithoutResult<TTagProjector, TTagGroup>> ProcessEvent(
        Event evt,
        SortableUniqueId safeWindowThreshold,
        DcbDomainTypes domainTypes)
    {
        var updatedInnerAccessor = ((ISafeAndUnsafeStateAccessor<GenericTagMultiProjector<TTagProjector, TTagGroup>>)Inner)
            .ProcessEvent(evt, safeWindowThreshold, domainTypes);

        if (updatedInnerAccessor is GenericTagMultiProjector<TTagProjector, TTagGroup> updatedInner)
        {
            return this with { Inner = updatedInner };
        }

        throw new InvalidOperationException(
            $"Unexpected projector type returned from ProcessEvent for {MultiProjectorName}: {updatedInnerAccessor.GetType().FullName}");
    }

    /// <summary>
    ///     Get all state payloads from current tag states
    /// </summary>
    public IEnumerable<ITagStatePayload> GetStatePayloads() =>
        Inner.GetStatePayloads();

    public IReadOnlyDictionary<Guid, TagState> GetCurrentTagStates() => Inner.GetCurrentTagStates();

    public int SafeVersion =>
        ((ISafeAndUnsafeStateAccessor<GenericTagMultiProjector<TTagProjector, TTagGroup>>)Inner).SafeVersion;
}
