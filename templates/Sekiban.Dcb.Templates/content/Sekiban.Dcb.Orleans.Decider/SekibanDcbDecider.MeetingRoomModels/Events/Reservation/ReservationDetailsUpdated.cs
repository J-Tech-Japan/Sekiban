using Dcb.MeetingRoomModels.Tags;
using Sekiban.Dcb.Events;
namespace Dcb.MeetingRoomModels.Events.Reservation;

/// <summary>
///     Event for updating reservation details (title, description, attendees, etc.)
/// </summary>
public record ReservationDetailsUpdated(
    Guid ReservationId,
    Guid RoomId,
    string? Title,
    string? Description,
    int? AttendeeCount,
    bool? HasExternalGuests,
    Dictionary<string, int>? RequiredEquipment) : IEventPayload
{
    public EventPayloadWithTags GetEventWithTags() =>
        new(this, [new ReservationTag(ReservationId), new RoomTag(RoomId)]);
}
