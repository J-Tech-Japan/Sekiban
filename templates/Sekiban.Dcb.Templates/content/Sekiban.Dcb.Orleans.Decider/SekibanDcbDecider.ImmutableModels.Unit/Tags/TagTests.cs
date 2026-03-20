using Dcb.ImmutableModels.Tags;

namespace SekibanDcbOrleans.ImmutableModels.Unit.Tags;

public class TagTests
{
    [Fact]
    public void ClassRoomTag_CreatesWithId()
    {
        var id = Guid.CreateVersion7();
        var tag = new ClassRoomTag(id);

        Assert.Equal(id, tag.GetId());
        Assert.Equal("ClassRoom", ClassRoomTag.TagGroupName);
        Assert.True(tag.IsConsistencyTag());
    }

    [Fact]
    public void ClassRoomTag_FromContent_ParsesGuid()
    {
        var id = Guid.CreateVersion7();
        var tag = ClassRoomTag.FromContent(id.ToString());

        Assert.Equal(id, tag.ClassRoomId);
    }

    [Fact]
    public void StudentTag_CreatesWithId()
    {
        var id = Guid.CreateVersion7();
        var tag = new StudentTag(id);

        Assert.Equal(id, tag.GetId());
        Assert.Equal("Student", StudentTag.TagGroupName);
        Assert.True(tag.IsConsistencyTag());
    }

    [Fact]
    public void StudentTag_FromContent_ParsesGuid()
    {
        var id = Guid.CreateVersion7();
        var tag = StudentTag.FromContent(id.ToString());

        Assert.Equal(id, tag.StudentId);
    }

    [Fact]
    public void WeatherForecastTag_CreatesWithId()
    {
        var id = Guid.CreateVersion7();
        var tag = new WeatherForecastTag(id);

        Assert.Equal(id, tag.GetId());
        Assert.Equal("WeatherForecast", WeatherForecastTag.TagGroupName);
        Assert.True(tag.IsConsistencyTag());
    }

    [Fact]
    public void WeatherForecastTag_FromContent_ParsesGuid()
    {
        var id = Guid.CreateVersion7();
        var tag = WeatherForecastTag.FromContent(id.ToString());

        Assert.Equal(id, tag.ForecastId);
    }

    [Fact]
    public void WeatherForecastTag_FromContent_ThrowsOnInvalidGuid()
    {
        Assert.Throws<ArgumentException>(() => WeatherForecastTag.FromContent("not-a-guid"));
    }

    [Fact]
    public void StudentCodeTag_CreatesWithStringId()
    {
        var tag = new StudentCodeTag("STU001");

        Assert.Equal("STU001", tag.GetId());
        Assert.Equal("StudentCode", StudentCodeTag.TagGroupName);
        Assert.False(tag.IsConsistencyTag());
    }

    [Fact]
    public void StudentCodeTag_FromContent_ParsesString()
    {
        var tag = StudentCodeTag.FromContent("STU002");

        Assert.Equal("STU002", tag.StudentCode);
    }

    [Fact]
    public void YearlyStudentsTag_CreatesWithIntId()
    {
        var tag = new YearlyStudentsTag(2026);

        Assert.Equal(2026, tag.GetId());
        Assert.Equal("YearlyStudents", YearlyStudentsTag.TagGroupName);
        Assert.True(tag.IsConsistencyTag());
    }

    [Fact]
    public void YearlyStudentsTag_FromContent_ParsesInt()
    {
        var tag = YearlyStudentsTag.FromContent("2026");

        Assert.Equal(2026, tag.Year);
    }

    [Fact]
    public void Tags_Equality_SameId_AreEqual()
    {
        var id = Guid.CreateVersion7();
        Assert.Equal(new ClassRoomTag(id), new ClassRoomTag(id));
        Assert.Equal(new StudentTag(id), new StudentTag(id));
        Assert.Equal(new WeatherForecastTag(id), new WeatherForecastTag(id));
    }

    [Fact]
    public void Tags_Equality_DifferentId_AreNotEqual()
    {
        Assert.NotEqual(new ClassRoomTag(Guid.CreateVersion7()), new ClassRoomTag(Guid.CreateVersion7()));
        Assert.NotEqual(new StudentTag(Guid.CreateVersion7()), new StudentTag(Guid.CreateVersion7()));
    }
}
