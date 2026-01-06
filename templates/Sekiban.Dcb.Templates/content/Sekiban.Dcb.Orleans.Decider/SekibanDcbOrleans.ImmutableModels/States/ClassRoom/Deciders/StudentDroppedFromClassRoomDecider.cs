using Dcb.ImmutableModels.Events.Enrollment;
namespace Dcb.ImmutableModels.States.ClassRoom.Deciders;

/// <summary>
///     Decider for StudentDroppedFromClassRoom event (affects ClassRoom state)
/// </summary>
public static class StudentDroppedFromClassRoomDecider
{
    /// <summary>
    ///     Validate preconditions for dropping student from AvailableClassRoomState
    /// </summary>
    /// <param name="state">Current classroom state</param>
    /// <param name="studentId">Student to drop</param>
    /// <exception cref="InvalidOperationException">When student is not enrolled</exception>
    public static void Validate(this AvailableClassRoomState state, Guid studentId)
    {
        if (!state.EnrolledStudentIds.Contains(studentId))
        {
            throw new InvalidOperationException(
                $"Student {studentId} is not enrolled in classroom {state.ClassRoomId}");
        }
    }

    /// <summary>
    ///     Validate preconditions for dropping student from FilledClassRoomState
    /// </summary>
    /// <param name="state">Current classroom state</param>
    /// <param name="studentId">Student to drop</param>
    /// <exception cref="InvalidOperationException">When student is not enrolled</exception>
    public static void Validate(this FilledClassRoomState state, Guid studentId)
    {
        if (!state.EnrolledStudentIds.Contains(studentId))
        {
            throw new InvalidOperationException(
                $"Student {studentId} is not enrolled in classroom {state.ClassRoomId}");
        }
    }

    /// <summary>
    ///     Apply StudentDroppedFromClassRoom event to AvailableClassRoomState
    /// </summary>
    public static AvailableClassRoomState Evolve(this AvailableClassRoomState state, StudentDroppedFromClassRoom dropped) =>
        state with
        {
            EnrolledStudentIds = state.EnrolledStudentIds.Where(id => id != dropped.StudentId).ToList()
        };

    /// <summary>
    ///     Apply StudentDroppedFromClassRoom event to FilledClassRoomState
    /// </summary>
    public static FilledClassRoomState Evolve(this FilledClassRoomState state, StudentDroppedFromClassRoom dropped) =>
        state with
        {
            EnrolledStudentIds = state.EnrolledStudentIds.Where(id => id != dropped.StudentId).ToList(),
            IsFull = false
        };
}
