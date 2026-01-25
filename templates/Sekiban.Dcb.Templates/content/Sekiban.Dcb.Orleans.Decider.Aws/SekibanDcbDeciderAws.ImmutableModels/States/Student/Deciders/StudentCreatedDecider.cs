using Dcb.ImmutableModels.Events.Student;
namespace Dcb.ImmutableModels.States.Student.Deciders;

/// <summary>
///     Decider for StudentCreated event
/// </summary>
public static class StudentCreatedDecider
{
    /// <summary>
    ///     Create a new StudentState from StudentCreated event
    /// </summary>
    public static StudentState Create(StudentCreated created) =>
        new(
            created.StudentId,
            created.Name,
            created.MaxClassCount,
            []);
}
