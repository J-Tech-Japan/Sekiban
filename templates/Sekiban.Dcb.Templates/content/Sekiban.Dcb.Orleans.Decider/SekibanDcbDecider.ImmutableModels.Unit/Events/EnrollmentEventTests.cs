using Dcb.ImmutableModels.Events.Enrollment;
using Dcb.ImmutableModels.Tags;

namespace SekibanDcbOrleans.ImmutableModels.Unit.Events;

public class EnrollmentEventTests
{
    [Fact]
    public void StudentEnrolledInClassRoom_GetEventWithTags_ReturnsBothTags()
    {
        var studentId = Guid.CreateVersion7();
        var classRoomId = Guid.CreateVersion7();
        var evt = new StudentEnrolledInClassRoom(studentId, classRoomId);

        var result = evt.GetEventWithTags();

        Assert.Equal(2, result.Tags.Count);
        Assert.Contains(result.Tags, t => t is StudentTag st && st.StudentId == studentId);
        Assert.Contains(result.Tags, t => t is ClassRoomTag ct && ct.ClassRoomId == classRoomId);
    }

    [Fact]
    public void StudentDroppedFromClassRoom_GetEventWithTags_ReturnsBothTags()
    {
        var studentId = Guid.CreateVersion7();
        var classRoomId = Guid.CreateVersion7();
        var evt = new StudentDroppedFromClassRoom(studentId, classRoomId);

        var result = evt.GetEventWithTags();

        Assert.Equal(2, result.Tags.Count);
        Assert.Contains(result.Tags, t => t is StudentTag st && st.StudentId == studentId);
        Assert.Contains(result.Tags, t => t is ClassRoomTag ct && ct.ClassRoomId == classRoomId);
    }
}
