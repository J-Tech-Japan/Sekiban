using Dcb.ImmutableModels.Events.Enrollment;
using Dcb.ImmutableModels.Events.Student;
using Dcb.ImmutableModels.States.Student;
using Dcb.ImmutableModels.States.Student.Deciders;

namespace SekibanDcbOrleans.ImmutableModels.Unit.States;

public class StudentDeciderTests
{
    [Fact]
    public void StudentCreatedDecider_Create_ReturnsInitialState()
    {
        var studentId = Guid.CreateVersion7();
        var created = new StudentCreated(studentId, "Alice", 5);

        var state = StudentCreatedDecider.Create(created);

        Assert.Equal(studentId, state.StudentId);
        Assert.Equal("Alice", state.Name);
        Assert.Equal(5, state.MaxClassCount);
        Assert.Empty(state.EnrolledClassRoomIds);
        Assert.Equal(5, state.GetRemaining());
    }

    [Fact]
    public void StudentEnrolledInClassRoom_Evolve_AddsClassRoom()
    {
        var studentId = Guid.CreateVersion7();
        var classRoomId = Guid.CreateVersion7();
        var state = new StudentState(studentId, "Alice", 5, []);
        var enrolled = new StudentEnrolledInClassRoom(studentId, classRoomId);

        var newState = StudentEnrolledInClassRoomDecider.Evolve(state, enrolled);

        Assert.Single(newState.EnrolledClassRoomIds);
        Assert.Contains(classRoomId, newState.EnrolledClassRoomIds);
        Assert.Equal(4, newState.GetRemaining());
    }

    [Fact]
    public void StudentEnrolledInClassRoom_Evolve_IdempotentForDuplicateClassRoom()
    {
        var studentId = Guid.CreateVersion7();
        var classRoomId = Guid.CreateVersion7();
        var state = new StudentState(studentId, "Alice", 5, [classRoomId]);
        var enrolled = new StudentEnrolledInClassRoom(studentId, classRoomId);

        var newState = StudentEnrolledInClassRoomDecider.Evolve(state, enrolled);

        Assert.Single(newState.EnrolledClassRoomIds);
    }

    [Fact]
    public void StudentEnrolledInClassRoom_Validate_ThrowsWhenMaxReached()
    {
        var studentId = Guid.CreateVersion7();
        var classRoomIds = Enumerable.Range(0, 5).Select(_ => Guid.CreateVersion7()).ToList();
        var state = new StudentState(studentId, "Alice", 5, classRoomIds);

        Assert.Throws<InvalidOperationException>(() =>
            StudentEnrolledInClassRoomDecider.Validate(state, Guid.CreateVersion7()));
    }

    [Fact]
    public void StudentEnrolledInClassRoom_Validate_ThrowsWhenAlreadyEnrolled()
    {
        var studentId = Guid.CreateVersion7();
        var classRoomId = Guid.CreateVersion7();
        var state = new StudentState(studentId, "Alice", 5, [classRoomId]);

        Assert.Throws<InvalidOperationException>(() =>
            StudentEnrolledInClassRoomDecider.Validate(state, classRoomId));
    }

    [Fact]
    public void StudentDroppedFromClassRoom_Evolve_RemovesClassRoom()
    {
        var studentId = Guid.CreateVersion7();
        var classRoomId = Guid.CreateVersion7();
        var state = new StudentState(studentId, "Alice", 5, [classRoomId]);
        var dropped = new StudentDroppedFromClassRoom(studentId, classRoomId);

        var newState = StudentDroppedFromClassRoomDecider.Evolve(state, dropped);

        Assert.Empty(newState.EnrolledClassRoomIds);
        Assert.Equal(5, newState.GetRemaining());
    }

    [Fact]
    public void StudentDroppedFromClassRoom_Validate_ThrowsWhenNotEnrolled()
    {
        var studentId = Guid.CreateVersion7();
        var state = new StudentState(studentId, "Alice", 5, []);

        Assert.Throws<InvalidOperationException>(() =>
            StudentDroppedFromClassRoomDecider.Validate(state, Guid.CreateVersion7()));
    }
}
