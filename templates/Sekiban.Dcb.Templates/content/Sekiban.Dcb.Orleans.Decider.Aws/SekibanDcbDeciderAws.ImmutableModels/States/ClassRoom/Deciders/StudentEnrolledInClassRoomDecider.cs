using Dcb.ImmutableModels.Events.Enrollment;
namespace Dcb.ImmutableModels.States.ClassRoom.Deciders;

/// <summary>
///     Decider for StudentEnrolledInClassRoom event (affects ClassRoom state)
/// </summary>
public static class StudentEnrolledInClassRoomDecider
{
    /// <summary>
    ///     Validate preconditions for enrolling student in AvailableClassRoomState
    /// </summary>
    /// <param name="state">Current classroom state</param>
    /// <param name="studentId">Student to enroll</param>
    /// <exception cref="InvalidOperationException">When classroom is full or student already enrolled</exception>
    public static void Validate(this AvailableClassRoomState state, Guid studentId)
    {
        if (state.GetRemaining() <= 0)
        {
            throw new InvalidOperationException(
                $"ClassRoom {state.ClassRoomId} is full (max: {state.MaxStudents})");
        }

        if (state.EnrolledStudentIds.Contains(studentId))
        {
            throw new InvalidOperationException(
                $"Student {studentId} is already enrolled in classroom {state.ClassRoomId}");
        }
    }

    /// <summary>
    ///     Validate preconditions for enrolling student in FilledClassRoomState
    /// </summary>
    /// <param name="state">Current classroom state</param>
    /// <param name="studentId">Student to enroll</param>
    /// <exception cref="InvalidOperationException">When classroom is full or student already enrolled</exception>
    public static void Validate(this FilledClassRoomState state, Guid studentId)
    {
        if (state.IsFull)
        {
            throw new InvalidOperationException(
                $"ClassRoom {state.ClassRoomId} is full");
        }

        if (state.EnrolledStudentIds.Contains(studentId))
        {
            throw new InvalidOperationException(
                $"Student {studentId} is already enrolled in classroom {state.ClassRoomId}");
        }
    }

    /// <summary>
    ///     Apply StudentEnrolledInClassRoom event to AvailableClassRoomState
    /// </summary>
    public static AvailableClassRoomState Evolve(this AvailableClassRoomState state, StudentEnrolledInClassRoom enrolled)
    {
        // Idempotency: if already enrolled, return same state
        if (state.EnrolledStudentIds.Contains(enrolled.StudentId))
            return state;

        return state with
        {
            EnrolledStudentIds = [..state.EnrolledStudentIds, enrolled.StudentId]
        };
    }

    /// <summary>
    ///     Apply StudentEnrolledInClassRoom event to FilledClassRoomState
    /// </summary>
    public static FilledClassRoomState Evolve(this FilledClassRoomState state, StudentEnrolledInClassRoom enrolled, int maxStudents)
    {
        // Idempotency: if already enrolled, return same state
        if (state.EnrolledStudentIds.Contains(enrolled.StudentId))
            return state;

        var newEnrolledStudents = state.EnrolledStudentIds.Append(enrolled.StudentId).ToList();
        return state with
        {
            EnrolledStudentIds = newEnrolledStudents,
            IsFull = newEnrolledStudents.Count >= maxStudents
        };
    }
}
