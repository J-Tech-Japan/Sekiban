using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using System.Text.Json;
using Xunit;

namespace Sekiban.Dcb.Tests;

public class TagProjectorNameMappingTests
{
    [Fact]
    public void TagStateId_FromProjector_UsesStaticProjectorName()
    {
        // Arrange
        var tag = new TestTag("test-id");
        
        // Act
        var tagStateId = TagStateId.FromProjector<TestProjector>(tag);
        
        // Assert
        Assert.Equal("TestProjector", tagStateId.TagProjectorName);
        Assert.Equal("TestTag:test-id:TestProjector", tagStateId.GetTagStateId());
    }
    
    [Fact]
    public void TagStateId_FromProjector_WithCustomProjectorName_UsesCustomName()
    {
        // Arrange
        var tag = new TestTag("test-id");
        
        // Act
        var tagStateId = TagStateId.FromProjector<CustomNameProjector>(tag);
        
        // Assert
        Assert.Equal("MyCustomProjectorName", tagStateId.TagProjectorName);
        Assert.Equal("TestTag:test-id:MyCustomProjectorName", tagStateId.GetTagStateId());
    }
    
    [Fact]
    public void TagStateId_Constructor_WithStringName_WorksCorrectly()
    {
        // Arrange
        var tag = new TestTag("test-id");
        
        // Act
        var tagStateId = new TagStateId(tag, "CustomName");
        
        // Assert
        Assert.Equal("CustomName", tagStateId.TagProjectorName);
        Assert.Equal("TestTag:test-id:CustomName", tagStateId.GetTagStateId());
    }
    
    // Test classes
    private class TestProjector : ITagProjector<TestProjector>
    {
        public static string ProjectorVersion => "1.0.0";
        public static string ProjectorName => nameof(TestProjector);
        public static ITagStatePayload Project(ITagStatePayload current, Event ev) => current;
    }
    
    private class CustomNameProjector : ITagProjector<CustomNameProjector>
    {
        public static string ProjectorVersion => "1.0.0";
        public static string ProjectorName => "MyCustomProjectorName";
        public static ITagStatePayload Project(ITagStatePayload current, Event ev) => current;
    }
    
    private record TestTag(string Id) : ITag
    {
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => "TestTag";
        public string GetTagContent() => Id;
        public string GetTag() => $"{GetTagGroup()}:{GetTagContent()}";
    }
}