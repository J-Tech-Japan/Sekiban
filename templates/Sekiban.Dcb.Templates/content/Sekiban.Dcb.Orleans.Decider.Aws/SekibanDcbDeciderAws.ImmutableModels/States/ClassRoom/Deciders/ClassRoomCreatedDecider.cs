using Dcb.ImmutableModels.Events.ClassRoom;
namespace Dcb.ImmutableModels.States.ClassRoom.Deciders;

/// <summary>
///     Decider for ClassRoomCreated event
/// </summary>
public static class ClassRoomCreatedDecider
{
    /// <summary>
    ///     Create a new AvailableClassRoomState from ClassRoomCreated event
    /// </summary>
    public static AvailableClassRoomState Create(ClassRoomCreated created) =>
        new(
            created.ClassRoomId,
            created.Name,
            created.MaxStudents,
            []);
}
