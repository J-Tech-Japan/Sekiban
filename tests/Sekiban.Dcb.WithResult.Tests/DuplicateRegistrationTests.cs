using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for duplicate type registration detection
/// </summary>
public class DuplicateRegistrationTests
{

    [Fact]
    public void SimpleEventTypes_Should_Throw_When_Registering_Different_Types_With_Same_Name()
    {
        // Arrange
        var eventTypes = new SimpleEventTypes();

        // Act - Register first type
        eventTypes.RegisterEventType<Namespace1.TestEvent>("TestEvent");

        // Act & Assert - Should throw when registering different type with same name
        var exception = Assert.Throws<InvalidOperationException>(() =>
            eventTypes.RegisterEventType<Namespace2.TestEvent>("TestEvent"));

        Assert.Contains("TestEvent", exception.Message);
        Assert.Contains("already registered", exception.Message);
        Assert.Contains("Namespace1", exception.Message);
        Assert.Contains("Namespace2", exception.Message);
    }

    [Fact]
    public void SimpleEventTypes_Should_Allow_Registering_Same_Type_Multiple_Times()
    {
        // Arrange
        var eventTypes = new SimpleEventTypes();

        // Act - Register same type multiple times
        eventTypes.RegisterEventType<Namespace1.TestEvent>("TestEvent");
        eventTypes.RegisterEventType<Namespace1.TestEvent>("TestEvent");

        // Assert - Should not throw
        var eventType = eventTypes.GetEventType("TestEvent");
        Assert.NotNull(eventType);
        Assert.Equal(typeof(Namespace1.TestEvent), eventType);
    }

    [Fact]
    public void SimpleTagProjectorTypes_Should_Throw_When_Registering_Different_Types_With_Same_Name()
    {
        // Arrange
        var projectorTypes = new SimpleTagProjectorTypes();

        // Act - Register first type
        projectorTypes.RegisterProjector<Namespace1.TestProjector>();

        // Act & Assert - Should throw when registering different type with same name
        var exception = Assert.Throws<InvalidOperationException>(() =>
            projectorTypes.RegisterProjector<Namespace2.TestProjector>());

        Assert.Contains("TestProjector", exception.Message);
        Assert.Contains("already registered with type", exception.Message);
        Assert.Contains("Namespace1", exception.Message);
        Assert.Contains("Namespace2", exception.Message);
    }

    [Fact]
    public void SimpleTagProjectorTypes_Should_Allow_Registering_Same_Type_Multiple_Times()
    {
        // Arrange
        var projectorTypes = new SimpleTagProjectorTypes();

        // Act - Register same type multiple times
        projectorTypes.RegisterProjector<Namespace1.TestProjector>();
        projectorTypes.RegisterProjector<Namespace1.TestProjector>();

        // Assert - Should not throw
        var projectorResult = projectorTypes.GetProjectorFunction("TestProjector");
        Assert.True(projectorResult.IsSuccess);
        Assert.NotNull(projectorResult.GetValue());
    }

    [Fact]
    public void SimpleTagStatePayloadTypes_Should_Throw_When_Registering_Different_Types_With_Same_Name()
    {
        // Arrange
        var payloadTypes = new SimpleTagStatePayloadTypes();

        // Act - Register first type
        payloadTypes.RegisterPayloadType<Namespace1.TestPayload>("TestPayload");

        // Act & Assert - Should throw when registering different type with same name
        var exception = Assert.Throws<InvalidOperationException>(() =>
            payloadTypes.RegisterPayloadType<Namespace2.TestPayload>("TestPayload"));

        Assert.Contains("TestPayload", exception.Message);
        Assert.Contains("already registered", exception.Message);
        Assert.Contains("Namespace1", exception.Message);
        Assert.Contains("Namespace2", exception.Message);
    }

    [Fact]
    public void SimpleTagStatePayloadTypes_Should_Allow_Registering_Same_Type_Multiple_Times()
    {
        // Arrange
        var payloadTypes = new SimpleTagStatePayloadTypes();

        // Act - Register same type multiple times
        payloadTypes.RegisterPayloadType<Namespace1.TestPayload>("TestPayload");
        payloadTypes.RegisterPayloadType<Namespace1.TestPayload>("TestPayload");

        // Assert - Should not throw
        var payloadType = payloadTypes.GetPayloadType("TestPayload");
        Assert.NotNull(payloadType);
        Assert.Equal(typeof(Namespace1.TestPayload), payloadType);
    }

    [Fact]
    public void Registration_Should_Use_Type_Name_When_Name_Not_Specified()
    {
        // Arrange
        var eventTypes = new SimpleEventTypes();
        var projectorTypes = new SimpleTagProjectorTypes();
        var payloadTypes = new SimpleTagStatePayloadTypes();

        // Act - Register without specifying name
        eventTypes.RegisterEventType<Namespace1.TestEvent>();
        projectorTypes.RegisterProjector<Namespace1.TestProjector>();
        payloadTypes.RegisterPayloadType<Namespace1.TestPayload>();

        // Assert - Should use type name
        Assert.NotNull(eventTypes.GetEventType("TestEvent"));
        Assert.True(projectorTypes.GetProjectorFunction("TestProjector").IsSuccess);
        Assert.NotNull(payloadTypes.GetPayloadType("TestPayload"));

        // Act & Assert - Should throw when trying to register different type with same type name
        Assert.Throws<InvalidOperationException>(() => eventTypes.RegisterEventType<Namespace2.TestEvent>());
        Assert.Throws<InvalidOperationException>(() => projectorTypes.RegisterProjector<Namespace2.TestProjector>());
        Assert.Throws<InvalidOperationException>(() => payloadTypes.RegisterPayloadType<Namespace2.TestPayload>());
    }
    #region Test Event Types
    public static class Namespace1
    {
        public record TestEvent(string Value) : IEventPayload;
        public class TestProjector : ITagProjector<TestProjector>
        {
            public static string ProjectorVersion => "1.0.0";
            public static string ProjectorName => nameof(TestProjector);
            public static ITagStatePayload Project(ITagStatePayload current, Event ev) => current;
        }
        public record TestPayload(string Data) : ITagStatePayload;
    }

    public static class Namespace2
    {
        public record TestEvent(int Number) : IEventPayload;
        public class TestProjector : ITagProjector<TestProjector>
        {
            public static string ProjectorVersion => "2.0.0";
            public static string ProjectorName => nameof(TestProjector);
            public static ITagStatePayload Project(ITagStatePayload current, Event ev) => current;
        }
        public record TestPayload(int Count) : ITagStatePayload;
    }
    #endregion
}
