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
    public Dictionary<Guid, ITagStatePayload> ClassRooms { get; init; } = [];

    public static string MultiProjectorName => nameof(ClassRoomListProjection);
    public static string MultiProjectorVersion => "1.0.0";

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

        var updatedClassRooms = new Dictionary<Guid, ITagStatePayload>(payload.ClassRooms);

        foreach (var tag in classRoomTags)
        {
            var classRoomId = tag.ClassRoomId;
            var currentState = updatedClassRooms.TryGetValue(classRoomId, out var existing)
                ? existing
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
                case EmptyTagStatePayload:
                    break;
                case AvailableClassRoomState available:
                    updatedClassRooms[classRoomId] = available;
                    break;
                case FilledClassRoomState filled:
                    updatedClassRooms[classRoomId] = filled;
                    break;
            }
        }

        return payload with { ClassRooms = updatedClassRooms };
    }

    /// <summary>
    ///     Get all class rooms
    /// </summary>
    public IReadOnlyList<ITagStatePayload> GetAllClassRooms() =>
        [.. ClassRooms.Values.Where(s => s is not EmptyTagStatePayload)];
}
