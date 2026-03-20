using Dcb.ImmutableModels.Events.ClassRoom;
using Dcb.ImmutableModels.Tags;

namespace SekibanDcbOrleans.ImmutableModels.Unit.Events;

public class ClassRoomEventTests
{
    [Fact]
    public void ClassRoomCreated_GetEventWithTags_ReturnsClassRoomTag()
    {
        var classRoomId = Guid.CreateVersion7();
        var evt = new ClassRoomCreated(classRoomId, "Math 101", 30);

        var result = evt.GetEventWithTags();

        var tag = Assert.Single(result.Tags);
        var classRoomTag = Assert.IsType<ClassRoomTag>(tag);
        Assert.Equal(classRoomId, classRoomTag.ClassRoomId);
    }

    [Fact]
    public void ClassRoomCreated_DefaultMaxStudents_Is10()
    {
        var evt = new ClassRoomCreated(Guid.CreateVersion7(), "History");

        Assert.Equal(10, evt.MaxStudents);
    }
}
