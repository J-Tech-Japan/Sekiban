using Dcb.ImmutableModels.Events.Enrollment;
namespace Dcb.ImmutableModels.States.Student.Deciders;

/// <summary>
///     Decider for StudentEnrolledInClassRoom event (affects StudentState)
/// </summary>
public static class StudentEnrolledInClassRoomDecider
{
    /// <summary>
    ///     Validate preconditions for enrolling student in classroom
    /// </summary>
    /// <param name="state">Current student state</param>
    /// <param name="classRoomId">ClassRoom to enroll in</param>
    /// <exception cref="InvalidOperationException">When student has reached max or already enrolled</exception>
    public static void Validate(this StudentState state, Guid classRoomId)
    {
        if (state.GetRemaining() <= 0)
        {
            throw new InvalidOperationException(
                $"Student {state.StudentId} has reached maximum class count of {state.MaxClassCount}");
        }

        if (state.EnrolledClassRoomIds.Contains(classRoomId))
        {
            throw new InvalidOperationException(
                $"Student {state.StudentId} is already enrolled in classroom {classRoomId}");
        }
    }

    /// <summary>
    ///     Apply StudentEnrolledInClassRoom event to StudentState
    /// </summary>
    public static StudentState Evolve(this StudentState state, StudentEnrolledInClassRoom enrolled)
    {
        // Idempotency: if already enrolled, return same state
        if (state.EnrolledClassRoomIds.Contains(enrolled.ClassRoomId))
            return state;

        return state with
        {
            EnrolledClassRoomIds = [..state.EnrolledClassRoomIds, enrolled.ClassRoomId]
        };
    }
}
