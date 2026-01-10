using Dcb.ImmutableModels.Events.Enrollment;
namespace Dcb.ImmutableModels.States.Student.Deciders;

/// <summary>
///     Decider for StudentDroppedFromClassRoom event (affects StudentState)
/// </summary>
public static class StudentDroppedFromClassRoomDecider
{
    /// <summary>
    ///     Validate preconditions for dropping student from classroom
    /// </summary>
    /// <param name="state">Current student state</param>
    /// <param name="classRoomId">ClassRoom to drop from</param>
    /// <exception cref="InvalidOperationException">When student is not enrolled</exception>
    public static void Validate(this StudentState state, Guid classRoomId)
    {
        if (!state.EnrolledClassRoomIds.Contains(classRoomId))
        {
            throw new InvalidOperationException(
                $"Student {state.StudentId} is not enrolled in classroom {classRoomId}");
        }
    }

    /// <summary>
    ///     Apply StudentDroppedFromClassRoom event to StudentState
    /// </summary>
    public static StudentState Evolve(this StudentState state, StudentDroppedFromClassRoom dropped) =>
        state with
        {
            EnrolledClassRoomIds = state.EnrolledClassRoomIds.Where(id => id != dropped.ClassRoomId).ToList()
        };
}
