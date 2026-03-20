using Dcb.ImmutableModels.Events.ClassRoom;
using Dcb.ImmutableModels.Events.Enrollment;
using Dcb.ImmutableModels.States.ClassRoom;
using Dcb.ImmutableModels.States.ClassRoom.Deciders;

namespace SekibanDcbOrleans.ImmutableModels.Unit.States;

public class ClassRoomDeciderTests
{
    [Fact]
    public void ClassRoomCreatedDecider_Create_ReturnsAvailableState()
    {
        var classRoomId = Guid.CreateVersion7();
        var created = new ClassRoomCreated(classRoomId, "Math 101", 30);

        var state = ClassRoomCreatedDecider.Create(created);

        Assert.Equal(classRoomId, state.ClassRoomId);
        Assert.Equal("Math 101", state.Name);
        Assert.Equal(30, state.MaxStudents);
        Assert.Empty(state.EnrolledStudentIds);
        Assert.Equal(30, state.GetRemaining());
    }

    [Fact]
    public void StudentEnrolledInClassRoom_Evolve_AddsStudent()
    {
        var classRoomId = Guid.CreateVersion7();
        var studentId = Guid.CreateVersion7();
        var state = new AvailableClassRoomState(classRoomId, "Math 101", 30, []);
        var enrolled = new StudentEnrolledInClassRoom(studentId, classRoomId);

        var newState = StudentEnrolledInClassRoomDecider.Evolve(state, enrolled);

        Assert.Single(newState.EnrolledStudentIds);
        Assert.Contains(studentId, newState.EnrolledStudentIds);
        Assert.Equal(29, newState.GetRemaining());
    }

    [Fact]
    public void StudentEnrolledInClassRoom_Evolve_IdempotentForDuplicateStudent()
    {
        var classRoomId = Guid.CreateVersion7();
        var studentId = Guid.CreateVersion7();
        var state = new AvailableClassRoomState(classRoomId, "Math 101", 30, [studentId]);
        var enrolled = new StudentEnrolledInClassRoom(studentId, classRoomId);

        var newState = StudentEnrolledInClassRoomDecider.Evolve(state, enrolled);

        Assert.Single(newState.EnrolledStudentIds);
    }

    [Fact]
    public void StudentEnrolledInClassRoom_Validate_ThrowsWhenFull()
    {
        var classRoomId = Guid.CreateVersion7();
        var existingStudents = Enumerable.Range(0, 2).Select(_ => Guid.CreateVersion7()).ToList();
        var state = new AvailableClassRoomState(classRoomId, "Math 101", 2, existingStudents);

        Assert.Throws<InvalidOperationException>(() =>
            StudentEnrolledInClassRoomDecider.Validate(state, Guid.CreateVersion7()));
    }

    [Fact]
    public void StudentEnrolledInClassRoom_Validate_ThrowsWhenAlreadyEnrolled()
    {
        var classRoomId = Guid.CreateVersion7();
        var studentId = Guid.CreateVersion7();
        var state = new AvailableClassRoomState(classRoomId, "Math 101", 30, [studentId]);

        Assert.Throws<InvalidOperationException>(() =>
            StudentEnrolledInClassRoomDecider.Validate(state, studentId));
    }

    [Fact]
    public void StudentDroppedFromClassRoom_Evolve_RemovesStudent()
    {
        var classRoomId = Guid.CreateVersion7();
        var studentId = Guid.CreateVersion7();
        var state = new AvailableClassRoomState(classRoomId, "Math 101", 30, [studentId]);
        var dropped = new StudentDroppedFromClassRoom(studentId, classRoomId);

        var newState = StudentDroppedFromClassRoomDecider.Evolve(state, dropped);

        Assert.Empty(newState.EnrolledStudentIds);
        Assert.Equal(30, newState.GetRemaining());
    }

    [Fact]
    public void StudentDroppedFromClassRoom_Validate_ThrowsWhenNotEnrolled()
    {
        var classRoomId = Guid.CreateVersion7();
        var state = new AvailableClassRoomState(classRoomId, "Math 101", 30, []);

        Assert.Throws<InvalidOperationException>(() =>
            StudentDroppedFromClassRoomDecider.Validate(state, Guid.CreateVersion7()));
    }

    [Fact]
    public void FilledClassRoomState_Evolve_StudentDropped_SetsIsFullToFalse()
    {
        var classRoomId = Guid.CreateVersion7();
        var studentId = Guid.CreateVersion7();
        var state = new FilledClassRoomState(classRoomId, "Math 101", [studentId], true);
        var dropped = new StudentDroppedFromClassRoom(studentId, classRoomId);

        var newState = StudentDroppedFromClassRoomDecider.Evolve(state, dropped);

        Assert.False(newState.IsFull);
        Assert.Empty(newState.EnrolledStudentIds);
    }

    [Fact]
    public void FilledClassRoomState_Validate_Enroll_ThrowsWhenFull()
    {
        var state = new FilledClassRoomState(Guid.CreateVersion7(), "Math 101", [], true);

        Assert.Throws<InvalidOperationException>(() =>
            StudentEnrolledInClassRoomDecider.Validate(state, Guid.CreateVersion7()));
    }
}
