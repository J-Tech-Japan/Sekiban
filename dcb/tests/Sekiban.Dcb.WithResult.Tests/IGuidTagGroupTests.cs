using Dcb.Domain.ClassRoom;
using Dcb.Domain.Student;
using Dcb.Domain.Weather;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for IGuidTagGroup interface
/// </summary>
public class IGuidTagGroupTests
{
    [Fact]
    public void ClassRoomTag_Should_Implement_IGuidTagGroup()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tag = new ClassRoomTag(id);

        // Act & Assert
        Assert.True(tag is IGuidTagGroup<ClassRoomTag>);
        Assert.Equal(id, tag.GetId());
        Assert.Equal(id, tag.ClassRoomId);
    }

    [Fact]
    public void StudentTag_Should_Implement_IGuidTagGroup()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tag = new StudentTag(id);

        // Act & Assert
        Assert.True(tag is IGuidTagGroup<StudentTag>);
        Assert.Equal(id, tag.GetId());
        Assert.Equal(id, tag.StudentId);
    }

    [Fact]
    public void WeatherForecastTag_Should_Implement_IGuidTagGroup()
    {
        // Arrange
        var id = Guid.NewGuid();
        var tag = new WeatherForecastTag(id);

        // Act & Assert
        Assert.True(tag is IGuidTagGroup<WeatherForecastTag>);
        Assert.Equal(id, tag.GetId());
        Assert.Equal(id, tag.ForecastId);
    }

    [Fact]
    public void IGuidTagGroup_GetId_Should_Match_GetTagContent()
    {
        // Arrange
        var id = Guid.NewGuid();
        var classRoomTag = new ClassRoomTag(id);
        var studentTag = new StudentTag(id);
        var weatherTag = new WeatherForecastTag(id);

        // Act & Assert
        Assert.Equal(classRoomTag.GetId().ToString(), ((ITag)classRoomTag).GetTagContent());
        Assert.Equal(studentTag.GetId().ToString(), ((ITag)studentTag).GetTagContent());
        Assert.Equal(weatherTag.GetId().ToString(), ((ITag)weatherTag).GetTagContent());
    }

    [Fact]
    public void IGuidTagGroup_Should_Work_With_FromContent()
    {
        // Arrange
        var id = Guid.NewGuid();
        var content = id.ToString();

        // Act
        var classRoomTag = ClassRoomTag.FromContent(content);
        var studentTag = StudentTag.FromContent(content);
        var weatherTag = WeatherForecastTag.FromContent(content);

        // Assert
        Assert.Equal(id, classRoomTag.GetId());
        Assert.Equal(id, studentTag.GetId());
        Assert.Equal(id, weatherTag.GetId());
    }

    [Fact]
    public void IGuidTagGroup_Can_Be_Used_Polymorphically()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        // Act
        var classRoomTag = new ClassRoomTag(id1);
        var studentTag = new StudentTag(id2);
        var weatherTag = new WeatherForecastTag(id3);

        // Assert - GetId() method works
        Assert.Equal(id1, classRoomTag.GetId());
        Assert.Equal(id2, studentTag.GetId());
        Assert.Equal(id3, weatherTag.GetId());

        // Also verify ITagGroup members still work
        Assert.Equal("ClassRoom", ClassRoomTag.TagGroupName);
        Assert.Equal("Student", StudentTag.TagGroupName);
        Assert.Equal("WeatherForecast", WeatherForecastTag.TagGroupName);

        Assert.True(classRoomTag.IsConsistencyTag());
        Assert.True(studentTag.IsConsistencyTag());
        Assert.True(weatherTag.IsConsistencyTag());
    }
}
