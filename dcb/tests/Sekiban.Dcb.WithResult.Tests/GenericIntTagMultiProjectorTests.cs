using Dcb.Domain.Student;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

public class GenericIntTagMultiProjectorTests
{
    private readonly DcbDomainTypes _domainTypes;

    public GenericIntTagMultiProjectorTests()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<StudentCreated>("StudentCreated");

        var tagTypes = new SimpleTagTypes();
        tagTypes.RegisterTagGroupType<YearlyStudentsTag>();

        var tagProjectorTypes = new SimpleTagProjectorTypes();
        tagProjectorTypes.RegisterProjector<StudentProjector>();

        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        tagStatePayloadTypes.RegisterPayloadType<StudentState>();

        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjectorWithCustomSerialization<
            GenericIntTagMultiProjector<StudentProjector, YearlyStudentsTag>>();

        _domainTypes = new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            multiProjectorTypes,
            new SimpleQueryTypes());
    }

    [Fact]
    public void IntKeyProjector_ProcessesIntTags()
    {
        // Arrange
        var projector = GenericIntTagMultiProjector<StudentProjector, YearlyStudentsTag>.GenerateInitialPayload();

        var year = 2024;

        // Create event with YearlyStudentsTag
        var studentEvent = CreateEvent(new StudentCreated(Guid.NewGuid(), "Alice"));

        // Act - Process event with YearlyStudentsTag
        var safeThreshold = SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-20), Guid.Empty);
        var result = GenericIntTagMultiProjector<StudentProjector, YearlyStudentsTag>.Project(
            projector,
            studentEvent,
            new List<ITag> { new YearlyStudentsTag(year) },
            _domainTypes,
            safeThreshold);

        Assert.True(result.IsSuccess);
        var updatedProjector = result.GetValue();

        // Assert
        var tagStates = updatedProjector.GetCurrentTagStates();

        Assert.NotNull(tagStates);
        Assert.NotEmpty(tagStates);
        Assert.Contains(year, tagStates.Keys);

        var tagState = tagStates[year];
        Assert.NotNull(tagState);

        var studentState = tagState.Payload as StudentState;
        Assert.NotNull(studentState);
        Assert.Equal("Alice", studentState.Name);
    }

    [Fact]
    public void IntKeyProjector_HandlesMultipleIntKeys()
    {
        // Arrange
        var projector = GenericIntTagMultiProjector<StudentProjector, YearlyStudentsTag>.GenerateInitialPayload();

        var year1 = 2024;
        var year2 = 2025;
        var year3 = 2026;

        var event1 = CreateEvent(new StudentCreated(Guid.NewGuid(), "Alice"));
        var event2 = CreateEvent(new StudentCreated(Guid.NewGuid(), "Bob"));
        var event3 = CreateEvent(new StudentCreated(Guid.NewGuid(), "Charlie"));

        var safeThreshold = SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-20), Guid.Empty);

        // Act
        var result1 = GenericIntTagMultiProjector<StudentProjector, YearlyStudentsTag>.Project(
            projector,
            event1,
            new List<ITag> { new YearlyStudentsTag(year1) },
            _domainTypes,
            safeThreshold);
        var projector1 = result1.GetValue();

        var result2 = GenericIntTagMultiProjector<StudentProjector, YearlyStudentsTag>.Project(
            projector1,
            event2,
            new List<ITag> { new YearlyStudentsTag(year2) },
            _domainTypes,
            safeThreshold);
        var projector2 = result2.GetValue();

        var result3 = GenericIntTagMultiProjector<StudentProjector, YearlyStudentsTag>.Project(
            projector2,
            event3,
            new List<ITag> { new YearlyStudentsTag(year3) },
            _domainTypes,
            safeThreshold);
        var projector3 = result3.GetValue();

        // Assert
        var tagStates = projector3.GetCurrentTagStates();

        Assert.Equal(3, tagStates.Count);
        Assert.Contains(year1, tagStates.Keys);
        Assert.Contains(year2, tagStates.Keys);
        Assert.Contains(year3, tagStates.Keys);

        var aliceState = tagStates[year1].Payload as StudentState;
        Assert.NotNull(aliceState);
        Assert.Equal("Alice", aliceState.Name);

        var bobState = tagStates[year2].Payload as StudentState;
        Assert.NotNull(bobState);
        Assert.Equal("Bob", bobState.Name);

        var charlieState = tagStates[year3].Payload as StudentState;
        Assert.NotNull(charlieState);
        Assert.Equal("Charlie", charlieState.Name);
    }

    [Fact]
    public void IntKeyProjector_SerializationRoundTrip()
    {
        // Arrange
        var projector = GenericIntTagMultiProjector<StudentProjector, YearlyStudentsTag>.GenerateInitialPayload();

        var year1 = 2024;
        var year2 = 2025;

        var event1 = CreateEvent(new StudentCreated(Guid.NewGuid(), "Alice"));
        var event2 = CreateEvent(new StudentCreated(Guid.NewGuid(), "Bob"));

        var safeThreshold = SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(20), Guid.Empty);

        var result1 = GenericIntTagMultiProjector<StudentProjector, YearlyStudentsTag>.Project(
            projector,
            event1,
            new List<ITag> { new YearlyStudentsTag(year1) },
            _domainTypes,
            safeThreshold);
        var projector1 = result1.GetValue();

        var result2 = GenericIntTagMultiProjector<StudentProjector, YearlyStudentsTag>.Project(
            projector1,
            event2,
            new List<ITag> { new YearlyStudentsTag(year2) },
            _domainTypes,
            safeThreshold);
        var projector2 = result2.GetValue();

        // Act - Serialize
        var serialized = GenericIntTagMultiProjector<StudentProjector, YearlyStudentsTag>.Serialize(
            _domainTypes,
            safeThreshold,
            projector2);

        Assert.NotNull(serialized);
        Assert.NotEmpty(serialized);

        // Act - Deserialize
        var deserialized = GenericIntTagMultiProjector<StudentProjector, YearlyStudentsTag>.Deserialize(
            _domainTypes,
            serialized);

        // Assert
        var originalStates = projector2.GetCurrentTagStates();
        var deserializedStates = deserialized.GetCurrentTagStates();

        Assert.Equal(originalStates.Count, deserializedStates.Count);
        Assert.Contains(year1, deserializedStates.Keys);
        Assert.Contains(year2, deserializedStates.Keys);

        var aliceState = deserializedStates[year1].Payload as StudentState;
        Assert.NotNull(aliceState);
        Assert.Equal("Alice", aliceState.Name);

        var bobState = deserializedStates[year2].Payload as StudentState;
        Assert.NotNull(bobState);
        Assert.Equal("Bob", bobState.Name);
    }

    [Fact]
    public void IntKeyProjector_GetTagId_ReturnsIntId()
    {
        // Arrange
        var year = 2030;
        var tag = new YearlyStudentsTag(year);

        // Act
        var id = tag.GetId();

        // Assert
        Assert.Equal(year, id);
        Assert.IsType<int>(id);
    }

    [Fact]
    public void IntKeyProjector_MultiProjectorName_ContainsIntTag()
    {
        // Act
        var name = GenericIntTagMultiProjector<StudentProjector, YearlyStudentsTag>.MultiProjectorName;

        // Assert
        Assert.Contains("GenericIntTagMultiProjector", name);
        Assert.Contains("StudentProjector", name);
        Assert.Contains("YearlyStudents", name);
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
}
