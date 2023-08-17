using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Types;
using System.Reflection;
namespace Sekiban.Core.Query.MultiProjections;

/// <summary>
///     Multi Projection State.
/// </summary>
/// <param name="Payload"></param>
/// <param name="LastEventId"></param>
/// <param name="LastSortableUniqueId"></param>
/// <param name="AppliedSnapshotVersion"></param>
/// <param name="Version"></param>
/// <param name="RootPartitionKey"></param>
/// <typeparam name="TProjectionPayload"></typeparam>
public record MultiProjectionState<TProjectionPayload>(
    TProjectionPayload Payload,
    Guid LastEventId,
    string LastSortableUniqueId,
    int AppliedSnapshotVersion,
    int Version,
    string RootPartitionKey) : IProjection where TProjectionPayload : IMultiProjectionPayloadCommon
{
    public MultiProjectionState() : this(
        GeneratePayload(),
        Guid.Empty,
        string.Empty,
        0,
        0,
        string.Empty)
    {
    }
    private static TProjectionPayload GeneratePayload()
    {
        var payloadType = typeof(TProjectionPayload);
        if (payloadType.IsMultiProjectionPayloadType())
        {
            var method = payloadType.GetMethod(
                nameof(IMultiProjectionPayload<TProjectionPayload>.CreateInitialPayload),
                BindingFlags.Static | BindingFlags.Public);
            var created = method?.Invoke(payloadType, new object?[] { });
            return created is TProjectionPayload projectionPayload
                ? projectionPayload
                : throw new SekibanMultiProjectionPayloadCreateFailedException(nameof(payloadType));
        }
        throw new SekibanMultiProjectionPayloadCreateFailedException(nameof(payloadType));
    }
    public string GetPayloadVersionIdentifier() => Payload.GetPayloadVersionIdentifier();
    public MultiProjectionState<TProjectionPayload> ApplyEvent(IEvent ev) =>
        this with
        {
            Payload = ((IMultiProjectionPayload<TProjectionPayload>)Payload).ApplyIEvent(ev),
            LastEventId = ev.Id,
            LastSortableUniqueId = ev.SortableUniqueId,
            Version = Version + 1
        };
}
