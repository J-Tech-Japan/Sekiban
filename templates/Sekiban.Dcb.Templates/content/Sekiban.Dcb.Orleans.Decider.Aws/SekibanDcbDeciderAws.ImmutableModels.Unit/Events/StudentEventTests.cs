using Dcb.ImmutableModels.Events.Student;
using Dcb.ImmutableModels.Tags;

namespace SekibanDcbOrleans.ImmutableModels.Unit.Events;

public class StudentEventTests
{
    [Fact]
    public void StudentCreated_GetEventWithTags_ReturnsStudentTag()
    {
        var studentId = Guid.CreateVersion7();
        var evt = new StudentCreated(studentId, "Alice", 5);

        var result = evt.GetEventWithTags();

        var tag = Assert.Single(result.Tags);
        var studentTag = Assert.IsType<StudentTag>(tag);
        Assert.Equal(studentId, studentTag.StudentId);
    }

    [Fact]
    public void StudentCreated_DefaultMaxClassCount_Is5()
    {
        var evt = new StudentCreated(Guid.CreateVersion7(), "Bob");

        Assert.Equal(5, evt.MaxClassCount);
    }
}
