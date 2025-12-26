using Dcb.Domain.ClassRoom;
using Dcb.Domain.Enrollment;
using Dcb.Domain.Student;
using Dcb.Domain.Weather;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

public class GenericTagMultiProjectorWithTagGroupTests
{
    private readonly DcbDomainTypes _domainTypes;

    public GenericTagMultiProjectorWithTagGroupTests()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<ClassRoomCreated>("ClassRoomCreated");
        eventTypes.RegisterEventType<StudentEnrolledInClassRoom>("StudentEnrolledInClassRoom");
        eventTypes.RegisterEventType<WeatherForecastCreated>("WeatherForecastCreated");

        var tagTypes = new SimpleTagTypes();
        tagTypes.RegisterTagGroupType<ClassRoomTag>();
        tagTypes.RegisterTagGroupType<StudentTag>();
        tagTypes.RegisterTagGroupType<WeatherForecastTag>();

        var tagProjectorTypes = new SimpleTagProjectorTypes();
        tagProjectorTypes.RegisterProjector<ClassRoomProjector>();
        tagProjectorTypes.RegisterProjector<StudentProjector>();
        tagProjectorTypes.RegisterProjector<WeatherForecastProjector>();

        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        tagStatePayloadTypes.RegisterPayloadType<AvailableClassRoomState>();
        tagStatePayloadTypes.RegisterPayloadType<FilledClassRoomState>();
        tagStatePayloadTypes.RegisterPayloadType<StudentState>();
        tagStatePayloadTypes.RegisterPayloadType<WeatherForecastState>();

        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        // Register type-specific multi-projectors
        multiProjectorTypes.RegisterProjector<GenericTagMultiProjector<ClassRoomProjector, ClassRoomTag>>();
        multiProjectorTypes.RegisterProjector<GenericTagMultiProjector<StudentProjector, StudentTag>>();
        multiProjectorTypes.RegisterProjector<GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>>();

        _domainTypes = new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            multiProjectorTypes,
            new SimpleQueryTypes());
    }

    [Fact]
    public void ClassRoomProjector_OnlyProcessesClassRoomTags()
    {
        // Arrange
        var projector = GenericTagMultiProjector<ClassRoomProjector, ClassRoomTag>.GenerateInitialPayload();

        var classRoomId = Guid.NewGuid();

        // Create event with ClassRoomTag
        var classRoomEvent = CreateEvent(new ClassRoomCreated(classRoomId, "Room 101", 30));

        // Act - Process event with ClassRoomTag
        var safeThreshold = SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-20), Guid.Empty);
        var result = GenericTagMultiProjector<ClassRoomProjector, ClassRoomTag>.Project(
            projector,
            classRoomEvent,
            new List<ITag> { new ClassRoomTag(classRoomId) },
            _domainTypes, safeThreshold);

        Assert.True(result.IsSuccess);
        var updatedProjector = result.GetValue();

        // Assert
        var tagStates = updatedProjector.GetCurrentTagStates();

        // Debug: Check state
        Assert.NotNull(tagStates);
        Assert.NotEmpty(tagStates);
        Assert.Contains(classRoomId, tagStates.Keys);

        var tagState = tagStates[classRoomId];
        Assert.NotNull(tagState);

        // Check that it's no longer EmptyTagStatePayload
        Assert.NotEqual(typeof(EmptyTagStatePayload), tagState.Payload.GetType());

        var classRoomState = tagState.Payload as AvailableClassRoomState;
        Assert.NotNull(classRoomState);
        Assert.Equal("Room 101", classRoomState.Name);
        Assert.Equal(30, classRoomState.MaxStudents);
    }

    [Fact]
    public void StudentProjector_OnlyProcessesStudentTags()
    {
        // Arrange
        var projector = GenericTagMultiProjector<StudentProjector, StudentTag>.GenerateInitialPayload();

        var classRoomId = Guid.NewGuid();
        var studentId1 = Guid.NewGuid();
        var studentId2 = Guid.NewGuid();

        // Create events with mixed tags
        var mixedEvent = CreateEvent(new StudentCreated(studentId1, "Alice"));

        var studentOnlyEvent = CreateEvent(new StudentCreated(studentId2, "Bob"));

        // Act
        var safeThreshold2 = SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-20), Guid.Empty);
        var result1 = GenericTagMultiProjector<StudentProjector, StudentTag>.Project(
            projector,
            mixedEvent,
            new List<ITag> { new StudentTag(studentId1), new ClassRoomTag(classRoomId) },
            _domainTypes, safeThreshold2);
        var projector1 = result1.GetValue();

        var result2 = GenericTagMultiProjector<StudentProjector, StudentTag>.Project(
            projector1,
            studentOnlyEvent,
            new List<ITag> { new StudentTag(studentId2) },
            _domainTypes, safeThreshold2);
        var projector2 = result2.GetValue();

        // Assert
        var tagStates = projector2.GetCurrentTagStates();

        // Should have both student states, but no classroom state
        Assert.Equal(2, tagStates.Count);
        Assert.Contains(studentId1, tagStates.Keys);
        Assert.Contains(studentId2, tagStates.Keys);
        Assert.DoesNotContain(classRoomId, tagStates.Keys);

        var aliceState = tagStates[studentId1].Payload as StudentState;
        Assert.NotNull(aliceState);
        Assert.Equal("Alice", aliceState.Name);

        var bobState = tagStates[studentId2].Payload as StudentState;
        Assert.NotNull(bobState);
        Assert.Equal("Bob", bobState.Name);
    }

    [Fact]
    public void GetTagId_UsesIGuidTagGroupGetIdMethod()
    {
        // Arrange
        var projector = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.GenerateInitialPayload();
        var forecastId = Guid.NewGuid();

        // Create event with WeatherForecastTag
        var weatherEvent = CreateEvent(new WeatherForecastCreated(forecastId, DateTime.Now, 20, "Cloudy"));

        // Act
        var safeThreshold3 = SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-20), Guid.Empty);
        var result = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Project(
            projector,
            weatherEvent,
            new List<ITag> { new WeatherForecastTag(forecastId) },
            _domainTypes, safeThreshold3);
        var updatedProjector = result.GetValue();

        // Assert
        var tagStates = updatedProjector.GetCurrentTagStates();

        // The key should be the exact GUID from GetId(), not a hash
        Assert.Single(tagStates);
        Assert.Contains(forecastId, tagStates.Keys);

        var weatherState = tagStates[forecastId].Payload as WeatherForecastState;
        Assert.NotNull(weatherState);
        Assert.Equal(20, weatherState.Temperature);
        Assert.Equal("Cloudy", weatherState.Summary);
    }

    private Event CreateEvent(IEventPayload payload)
    {
        var sortableId = SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid());
        return new Event(
            payload,
            sortableId,
            payload.GetType().Name,
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "TestUser"),
            new List<string>());
    }

    // Test event payloads - using test-specific events to avoid conflicts
    public record StudentCreated(Guid StudentId, string Name) : IEventPayload;
    public record WeatherForecastCreated(Guid ForecastId, DateTime Date, int Temperature, string Summary)
        : IEventPayload;

    // Mock StudentProjector (since it wasn't defined in the original code)
    public class StudentProjector : ITagProjector<StudentProjector>
    {
        public static string ProjectorVersion => "1.0.0";
        public static string ProjectorName => nameof(StudentProjector);

        public static ITagStatePayload Project(ITagStatePayload current, Event ev) =>
            (current, ev.Payload) switch
            {
                (EmptyTagStatePayload, StudentCreated created) => new StudentState(created.StudentId, created.Name),
                _ => current
            };
    }

    // Mock StudentState
    public record StudentState(Guid StudentId, string Name) : ITagStatePayload;

    // Mock WeatherForecastProjector
    public class WeatherForecastProjector : ITagProjector<WeatherForecastProjector>
    {
        public static string ProjectorVersion => "1.0.0";
        public static string ProjectorName => nameof(WeatherForecastProjector);

        public static ITagStatePayload Project(ITagStatePayload current, Event ev) =>
            (current, ev.Payload) switch
            {
                (EmptyTagStatePayload, WeatherForecastCreated created) => new WeatherForecastState(
                    created.ForecastId,
                    created.Date,
                    created.Temperature,
                    created.Summary,
                    false),
                _ => current
            };
    }

    // Mock WeatherForecastState
    public record WeatherForecastState(Guid Id, DateTime Date, int Temperature, string Summary, bool IsDeleted)
        : ITagStatePayload;
}
