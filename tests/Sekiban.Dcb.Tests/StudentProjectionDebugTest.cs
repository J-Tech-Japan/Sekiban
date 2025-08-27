using Dcb.Domain;
using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using Xunit;
using Xunit.Abstractions;

namespace Sekiban.Dcb.Tests;

public class StudentProjectionDebugTest
{
    private readonly ITestOutputHelper _output;
    private readonly DcbDomainTypes _domainTypes;
    private readonly InMemoryEventStore _eventStore;
    private readonly TimeProvider _timeProvider;

    public StudentProjectionDebugTest(ITestOutputHelper output)
    {
        _output = output;
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<StudentCreated>("StudentCreated");
        
        var tagTypes = new SimpleTagTypes();
        tagTypes.RegisterTagGroupType<StudentTag>();
        
        var tagProjectorTypes = new SimpleTagProjectorTypes();
        tagProjectorTypes.RegisterProjector<StudentProjector>();
        
        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        tagStatePayloadTypes.RegisterPayloadType<StudentState>();
        
        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<GenericTagMultiProjector<StudentProjector, StudentTag>>();
        
        _domainTypes = new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            multiProjectorTypes,
            new SimpleQueryTypes());
        _eventStore = new InMemoryEventStore();
        _timeProvider = TimeProvider.System;
    }

    [Fact]
    public void Test_GenericTagMultiProjector_With_StudentCreated_Event()
    {
        _output.WriteLine("=== Starting Test_GenericTagMultiProjector_With_StudentCreated_Event ===");
        
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var studentCreated = new StudentCreated(studentId, "Alice", 5);
        
        var ev = new Event(
            Payload: studentCreated,
            SortableUniqueIdValue: SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
            EventType: studentCreated.GetType().Name,
            Id: Guid.NewGuid(),
            EventMetadata: new EventMetadata(null, null, "test"),
            Tags: new List<string> { studentTag.GetTag() }
        );

        _output.WriteLine($"Created event: {ev.EventType} with tag: {studentTag.GetTag()}");

        // Create initial projector state
        var projector = GenericTagMultiProjector<StudentProjector, StudentTag>.GenerateInitialPayload();
        
        // Prepare projection functions for safe state queries
        var threshold = SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-20), Guid.Empty);
        Func<Event, IEnumerable<Guid>> getIds = evt => new[] { studentId };
        Func<Guid, TagState?, Event, TagState?> projectTagState = (id, current, evt) =>
        {
            if (current == null)
            {
                var tagStateId = new TagStateId(new StudentTag(id), "StudentProjector");
                current = TagState.GetEmpty(tagStateId);
            }
            var newPayload = StudentProjector.Project(current.Payload, evt);
            return current with
            {
                Payload = newPayload,
                Version = current.Version + 1,
                LastSortedUniqueId = evt.SortableUniqueIdValue
            };
        };
        
        _output.WriteLine($"Initial projector - Current states: {projector.GetCurrentTagStates().Count}, Safe states: {projector.GetSafeTagStates(threshold, getIds, projectTagState).Count}");

        // Act - Call the static Project method directly
        var tags = new List<ITag> { studentTag };
        var safeThreshold = SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-20), Guid.Empty);
        var result = GenericTagMultiProjector<StudentProjector, StudentTag>.Project(
            projector, 
            ev, 
            tags, 
            _domainTypes, 
            safeThreshold);

        // Assert
        Assert.True(result.IsSuccess);
        var projected = result.GetValue();
        
        _output.WriteLine($"After projection - Current states: {projected.GetCurrentTagStates().Count}, Safe states: {projected.GetSafeTagStates(threshold, getIds, projectTagState).Count}");
        
        var currentStates = projected.GetCurrentTagStates();
        Assert.Single(currentStates);
        Assert.True(currentStates.ContainsKey(studentId));
        
        var tagState = currentStates[studentId];
        Assert.NotNull(tagState);
        Assert.IsType<StudentState>(tagState.Payload);
        
        var studentState = (StudentState)tagState.Payload;
        Assert.Equal(studentId, studentState.StudentId);
        Assert.Equal("Alice", studentState.Name);
        Assert.Equal(5, studentState.MaxClassCount);
        Assert.Empty(studentState.EnrolledClassRoomIds);
        
        // Test GetStatePayloads
        var payloads = projected.GetStatePayloads().ToList();
        _output.WriteLine($"GetStatePayloads returned {payloads.Count} items");
        Assert.Single(payloads);
        Assert.IsType<StudentState>(payloads[0]);
        
        _output.WriteLine("=== Test completed successfully ===");
    }

    [Fact]
    public void Test_ProcessEvent_Through_ISafeAndUnsafeStateAccessor()
    {
        _output.WriteLine("=== Starting Test_ProcessEvent_Through_ISafeAndUnsafeStateAccessor ===");
        
        // Arrange
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var studentCreated = new StudentCreated(studentId, "Bob", 3);
        
        var ev = new Event(
            Payload: studentCreated,
            SortableUniqueIdValue: SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
            EventType: studentCreated.GetType().Name,
            Id: Guid.NewGuid(),
            EventMetadata: new EventMetadata(null, null, "test"),
            Tags: new List<string> { studentTag.GetTag() }
        );

        _output.WriteLine($"Created event: {ev.EventType} with tag: {studentTag.GetTag()}");

        // Create initial projector state
        var projector = GenericTagMultiProjector<StudentProjector, StudentTag>.GenerateInitialPayload();
        ISafeAndUnsafeStateAccessor<GenericTagMultiProjector<StudentProjector, StudentTag>> accessor = projector;
        
        // Calculate safe window threshold
        var safeWindowThreshold = new SortableUniqueId(
            SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-20), Guid.Empty));

        // Act - Call ProcessEvent through the interface
        _output.WriteLine("Calling ProcessEvent through ISafeAndUnsafeStateAccessor interface...");
        var processed = accessor.ProcessEvent(ev, safeWindowThreshold, _domainTypes, _timeProvider);
        
        // Assert
        Assert.NotNull(processed);
        
        // Cast back to the concrete type to check state
        var concreteResult = processed as GenericTagMultiProjector<StudentProjector, StudentTag>;
        Assert.NotNull(concreteResult);
        
        _output.WriteLine($"After ProcessEvent - Current states: {concreteResult.GetCurrentTagStates().Count}");
        
        var currentStates = concreteResult.GetCurrentTagStates();
        Assert.Single(currentStates);
        Assert.True(currentStates.ContainsKey(studentId));
        
        // Test GetStatePayloads
        var payloads = concreteResult.GetStatePayloads().ToList();
        _output.WriteLine($"GetStatePayloads returned {payloads.Count} items");
        Assert.Single(payloads);
        
        var studentState = payloads[0] as StudentState;
        Assert.NotNull(studentState);
        Assert.Equal(studentId, studentState.StudentId);
        Assert.Equal("Bob", studentState.Name);
        Assert.Equal(3, studentState.MaxClassCount);
        
        _output.WriteLine("=== Test completed successfully ===");
    }
}