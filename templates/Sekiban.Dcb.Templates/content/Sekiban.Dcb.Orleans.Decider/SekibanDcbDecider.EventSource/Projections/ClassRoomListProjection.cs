using Dcb.ImmutableModels.Events.ClassRoom;
using Dcb.ImmutableModels.Events.Enrollment;
using Dcb.ImmutableModels.States.ClassRoom;
using Dcb.ImmutableModels.States.ClassRoom.Deciders;
using Dcb.ImmutableModels.Tags;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Dcb.EventSource.Projections;

/// <summary>
///     ClassRoom list projection for multi-projection queries
/// </summary>
public record ClassRoomListProjection : IMultiProjector<ClassRoomListProjection>
{
    // Concrete state types (not ITagStatePayload) so the projection snapshot can be serialized and
    // restored; an interface-typed dictionary cannot be deserialized and forces a full replay.
    public Dictionary<Guid, AvailableClassRoomState> AvailableClassRooms { get; init; } = [];
    public Dictionary<Guid, FilledClassRoomState> FilledClassRooms { get; init; } = [];

    public static string MultiProjectorName => nameof(ClassRoomListProjection);
    public static string MultiProjectorVersion => "2.0.0";

    public static ClassRoomListProjection GenerateInitialPayload() => new();

    public static ClassRoomListProjection Project(
        ClassRoomListProjection payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        var classRoomTags = tags.OfType<ClassRoomTag>().ToList();
        if (classRoomTags.Count == 0) return payload;

        var available = new Dictionary<Guid, AvailableClassRoomState>(payload.AvailableClassRooms);
        var filled = new Dictionary<Guid, FilledClassRoomState>(payload.FilledClassRooms);

        foreach (var tag in classRoomTags)
        {
            var classRoomId = tag.ClassRoomId;
            ITagStatePayload currentState = available.TryGetValue(classRoomId, out var availableState)
                ? availableState
                : filled.TryGetValue(classRoomId, out var filledState)
                    ? filledState
                    : new EmptyTagStatePayload();

            ITagStatePayload newState = (currentState, ev.Payload) switch
            {
                (EmptyTagStatePayload, ClassRoomCreated created) => ClassRoomCreatedDecider.Create(created),
                (AvailableClassRoomState state, StudentEnrolledInClassRoom enrolled) when state.GetRemaining() > 1 =>
                    state.Evolve(enrolled),
                (AvailableClassRoomState state, StudentEnrolledInClassRoom enrolled) when state.GetRemaining() == 1 =>
                    new FilledClassRoomState(
                        state.ClassRoomId,
                        state.Name,
                        state.Evolve(enrolled).EnrolledStudentIds,
                        true),
                (AvailableClassRoomState state, StudentDroppedFromClassRoom dropped) =>
                    state.Evolve(dropped),
                (FilledClassRoomState state, StudentDroppedFromClassRoom dropped) =>
                    new AvailableClassRoomState(
                        state.ClassRoomId,
                        state.Name,
                        state.EnrolledStudentIds.Count,
                        state.Evolve(dropped).EnrolledStudentIds),
                _ => currentState
            };

            switch (newState)
            {
                case AvailableClassRoomState nextAvailable:
                    filled.Remove(classRoomId);
                    available[classRoomId] = nextAvailable;
                    break;
                case FilledClassRoomState nextFilled:
                    available.Remove(classRoomId);
                    filled[classRoomId] = nextFilled;
                    break;
            }
        }

        return payload with { AvailableClassRooms = available, FilledClassRooms = filled };
    }

    /// <summary>
    ///     Get all class rooms (available and filled)
    /// </summary>
    public IReadOnlyList<ITagStatePayload> GetAllClassRooms() =>
        [.. AvailableClassRooms.Values.Cast<ITagStatePayload>(), .. FilledClassRooms.Values];
}
