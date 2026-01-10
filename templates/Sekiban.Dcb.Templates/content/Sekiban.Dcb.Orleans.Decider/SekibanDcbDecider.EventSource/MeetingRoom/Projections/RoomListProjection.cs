using Dcb.MeetingRoomModels.Events.Room;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.States.Room.Deciders;
using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.MeetingRoom.Projections;

/// <summary>
///     Room list projection for multi-projection queries
/// </summary>
public record RoomListProjection : IMultiProjector<RoomListProjection>
{
    public Dictionary<Guid, RoomState> Rooms { get; init; } = [];

    public static string MultiProjectorName => nameof(RoomListProjection);
    public static string MultiProjectorVersion => "1.0.0";

    public static RoomListProjection GenerateInitialPayload() => new();

    public static RoomListProjection Project(
        RoomListProjection payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        var roomTags = tags.OfType<RoomTag>().ToList();
        if (roomTags.Count == 0) return payload;

        var updatedRooms = new Dictionary<Guid, RoomState>(payload.Rooms);

        foreach (var tag in roomTags)
        {
            var roomId = tag.RoomId;
            var currentState = updatedRooms.TryGetValue(roomId, out var existing)
                ? existing
                : RoomState.Empty;

            var newState = ev.Payload switch
            {
                RoomCreated created => RoomCreatedDecider.Create(created),
                RoomUpdated updated => RoomUpdatedDecider.Evolve(currentState, updated),
                RoomDeactivated deactivated => RoomDeactivatedDecider.Evolve(currentState, deactivated),
                RoomReactivated reactivated => RoomReactivatedDecider.Evolve(currentState, reactivated),
                _ => currentState
            };

            if (newState != RoomState.Empty)
            {
                updatedRooms[roomId] = newState;
            }
        }

        return payload with { Rooms = updatedRooms };
    }

    /// <summary>
    ///     Get all active rooms
    /// </summary>
    public IReadOnlyList<RoomState> GetActiveRooms() =>
        [.. Rooms.Values.Where(r => r.IsActive).OrderBy(r => r.Name, StringComparer.Ordinal)];

    /// <summary>
    ///     Get all rooms including inactive
    /// </summary>
    public IReadOnlyList<RoomState> GetAllRooms() =>
        [.. Rooms.Values.OrderBy(r => r.Name, StringComparer.Ordinal)];

    /// <summary>
    ///     Get room by ID
    /// </summary>
    public RoomState? GetRoom(Guid roomId) =>
        Rooms.TryGetValue(roomId, out var room) ? room : null;
}
