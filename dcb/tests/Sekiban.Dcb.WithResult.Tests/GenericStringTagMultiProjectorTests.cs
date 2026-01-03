using Dcb.Domain.Student;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

public class GenericStringTagMultiProjectorTests
{
    private readonly DcbDomainTypes _domainTypes;

    public GenericStringTagMultiProjectorTests()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<StudentCreated>("StudentCreated");

        var tagTypes = new SimpleTagTypes();
        tagTypes.RegisterTagGroupType<StudentCodeTag>();

        var tagProjectorTypes = new SimpleTagProjectorTypes();
        tagProjectorTypes.RegisterProjector<StudentProjector>();

        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        tagStatePayloadTypes.RegisterPayloadType<StudentState>();

        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjectorWithCustomSerialization<
            GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>>();

        _domainTypes = new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            multiProjectorTypes,
            new SimpleQueryTypes());
    }

    [Fact]
    public void StringKeyProjector_ProcessesStringTags()
    {
        // Arrange
        var projector = GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.GenerateInitialPayload();

        var studentCode = "STU001";

        // Create event with StudentCodeTag
        var studentEvent = CreateEvent(new StudentCreated(Guid.NewGuid(), "Alice"));

        // Act - Process event with StudentCodeTag
        var safeThreshold = SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-20), Guid.Empty);
        var result = GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.Project(
            projector,
            studentEvent,
            new List<ITag> { new StudentCodeTag(studentCode) },
            _domainTypes,
            safeThreshold);

        Assert.True(result.IsSuccess);
        var updatedProjector = result.GetValue();

        // Assert
        var tagStates = updatedProjector.GetCurrentTagStates();

        Assert.NotNull(tagStates);
        Assert.NotEmpty(tagStates);
        Assert.Contains(studentCode, tagStates.Keys);

        var tagState = tagStates[studentCode];
        Assert.NotNull(tagState);

        var studentState = tagState.Payload as StudentState;
        Assert.NotNull(studentState);
        Assert.Equal("Alice", studentState.Name);
    }

    [Fact]
    public void StringKeyProjector_HandlesMultipleStringKeys()
    {
        // Arrange
        var projector = GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.GenerateInitialPayload();

        var studentCode1 = "STU001";
        var studentCode2 = "STU002";
        var studentCode3 = "STU003";

        var event1 = CreateEvent(new StudentCreated(Guid.NewGuid(), "Alice"));
        var event2 = CreateEvent(new StudentCreated(Guid.NewGuid(), "Bob"));
        var event3 = CreateEvent(new StudentCreated(Guid.NewGuid(), "Charlie"));

        var safeThreshold = SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-20), Guid.Empty);

        // Act
        var result1 = GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.Project(
            projector,
            event1,
            new List<ITag> { new StudentCodeTag(studentCode1) },
            _domainTypes,
            safeThreshold);
        var projector1 = result1.GetValue();

        var result2 = GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.Project(
            projector1,
            event2,
            new List<ITag> { new StudentCodeTag(studentCode2) },
            _domainTypes,
            safeThreshold);
        var projector2 = result2.GetValue();

        var result3 = GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.Project(
            projector2,
            event3,
            new List<ITag> { new StudentCodeTag(studentCode3) },
            _domainTypes,
            safeThreshold);
        var projector3 = result3.GetValue();

        // Assert
        var tagStates = projector3.GetCurrentTagStates();

        Assert.Equal(3, tagStates.Count);
        Assert.Contains(studentCode1, tagStates.Keys);
        Assert.Contains(studentCode2, tagStates.Keys);
        Assert.Contains(studentCode3, tagStates.Keys);

        var aliceState = tagStates[studentCode1].Payload as StudentState;
        Assert.NotNull(aliceState);
        Assert.Equal("Alice", aliceState.Name);

        var bobState = tagStates[studentCode2].Payload as StudentState;
        Assert.NotNull(bobState);
        Assert.Equal("Bob", bobState.Name);

        var charlieState = tagStates[studentCode3].Payload as StudentState;
        Assert.NotNull(charlieState);
        Assert.Equal("Charlie", charlieState.Name);
    }

    [Fact]
    public void StringKeyProjector_SerializationRoundTrip()
    {
        // Arrange
        var projector = GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.GenerateInitialPayload();

        var studentCode1 = "STU001";
        var studentCode2 = "STU002";

        var event1 = CreateEvent(new StudentCreated(Guid.NewGuid(), "Alice"));
        var event2 = CreateEvent(new StudentCreated(Guid.NewGuid(), "Bob"));

        var safeThreshold = SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(20), Guid.Empty);

        var result1 = GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.Project(
            projector,
            event1,
            new List<ITag> { new StudentCodeTag(studentCode1) },
            _domainTypes,
            safeThreshold);
        var projector1 = result1.GetValue();

        var result2 = GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.Project(
            projector1,
            event2,
            new List<ITag> { new StudentCodeTag(studentCode2) },
            _domainTypes,
            safeThreshold);
        var projector2 = result2.GetValue();

        // Act - Serialize
        var serialized = GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.Serialize(
            _domainTypes,
            safeThreshold,
            projector2);

        Assert.NotNull(serialized);
        Assert.NotEmpty(serialized.Data);

        // Act - Deserialize
        var deserialized = GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.Deserialize(
            _domainTypes,
            safeThreshold,
            serialized.Data);

        // Assert
        var originalStates = projector2.GetCurrentTagStates();
        var deserializedStates = deserialized.GetCurrentTagStates();

        Assert.Equal(originalStates.Count, deserializedStates.Count);
        Assert.Contains(studentCode1, deserializedStates.Keys);
        Assert.Contains(studentCode2, deserializedStates.Keys);

        var aliceState = deserializedStates[studentCode1].Payload as StudentState;
        Assert.NotNull(aliceState);
        Assert.Equal("Alice", aliceState.Name);

        var bobState = deserializedStates[studentCode2].Payload as StudentState;
        Assert.NotNull(bobState);
        Assert.Equal("Bob", bobState.Name);
    }

    [Fact]
    public void StringKeyProjector_GetTagId_ReturnsStringId()
    {
        // Arrange
        var studentCode = "STU999";
        var tag = new StudentCodeTag(studentCode);

        // Act
        var id = tag.GetId();

        // Assert
        Assert.Equal(studentCode, id);
        Assert.IsType<string>(id);
    }

    [Fact]
    public void StringKeyProjector_MultiProjectorName_ContainsStringTag()
    {
        // Act
        var name = GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.MultiProjectorName;

        // Assert
        Assert.Contains("GenericStringTagMultiProjector", name);
        Assert.Contains("StudentProjector", name);
        Assert.Contains("StudentCode", name);
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
