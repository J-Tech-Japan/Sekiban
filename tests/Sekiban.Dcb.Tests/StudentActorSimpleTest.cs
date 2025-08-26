using Dcb.Domain.Enrollment;
using Dcb.Domain.Student;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
using Xunit;
using Xunit.Abstractions;

namespace Sekiban.Dcb.Tests;

public class StudentActorSimpleTest
{
    private readonly ITestOutputHelper _output;

    public StudentActorSimpleTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Single_Student_Should_Be_Visible_In_Actor_State()
    {
        // Arrange - Create domain with Student types
        var domain = DcbDomainTypes.Simple(builder =>
        {
            builder.EventTypes.RegisterEventType<StudentCreated>();
            builder.EventTypes.RegisterEventType<StudentEnrolledInClassRoom>();
            builder.TagTypes.RegisterTagGroupType<StudentTag>();
            builder.TagProjectorTypes.RegisterProjector<StudentProjector>();
            builder.TagStatePayloadTypes.RegisterPayloadType<StudentState>();
            builder.MultiProjectorTypes.RegisterProjector<GenericTagMultiProjector<StudentProjector, StudentTag>>();
        });

        var projectorName = GenericTagMultiProjector<StudentProjector, StudentTag>.MultiProjectorName;
        _output.WriteLine($"Using projector: {projectorName}");
        
        var actor = new GeneralMultiProjectionActor(domain, projectorName);

        // Create a single student event
        var studentId = Guid.NewGuid();
        var studentTag = new StudentTag(studentId);
        var studentCreated = new StudentCreated(studentId, "Alice", 5);
        
        var ev = new Event(
            Payload: studentCreated,
            SortableUniqueIdValue: SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
            EventType: "StudentCreated",
            Id: Guid.NewGuid(),
            EventMetadata: new EventMetadata(null, null, "test"),
            Tags: new List<string> { ((ITag)studentTag).GetTag() }
        );

        // Act - Add event to actor
        _output.WriteLine($"Adding event: {ev.EventType} for student {studentId}");
        await actor.AddEventsAsync(new[] { ev });

        // Get state from actor
        var stateResult = await actor.GetStateAsync();
        Assert.True(stateResult.IsSuccess);
        var state = stateResult.GetValue();

        // Assert - Check if the student is in the state
        Assert.NotNull(state);
        Assert.NotNull(state.Payload);
        
        if (state.Payload is GenericTagMultiProjector<StudentProjector, StudentTag> projector)
        {
            var currentStates = projector.GetCurrentTagStates();
            var payloads = projector.GetStatePayloads().ToList();
            
            _output.WriteLine($"✅ State contains: {currentStates.Count} tag states, {payloads.Count} payloads");
            
            // Should have exactly 1 student
            Assert.Single(currentStates);
            Assert.Single(payloads);
            
            var studentState = payloads[0] as StudentState;
            Assert.NotNull(studentState);
            Assert.Equal(studentId, studentState.StudentId);
            Assert.Equal("Alice", studentState.Name);
            Assert.Equal(5, studentState.MaxClassCount);
            
            _output.WriteLine($"✅ Student found in state: {studentState.Name} (ID: {studentState.StudentId})");
        }
        else
        {
            _output.WriteLine($"❌ State payload type: {state.Payload?.GetType().Name ?? "null"}");
            Assert.True(false, "State payload is not the expected GenericTagMultiProjector type");
        }
    }
    
    [Fact]
    public async Task Two_Students_Should_Both_Be_Visible_In_Actor_State()
    {
        // Arrange
        var domain = DcbDomainTypes.Simple(builder =>
        {
            builder.EventTypes.RegisterEventType<StudentCreated>();
            builder.TagTypes.RegisterTagGroupType<StudentTag>();
            builder.TagProjectorTypes.RegisterProjector<StudentProjector>();
            builder.TagStatePayloadTypes.RegisterPayloadType<StudentState>();
            builder.MultiProjectorTypes.RegisterProjector<GenericTagMultiProjector<StudentProjector, StudentTag>>();
        });

        var projectorName = GenericTagMultiProjector<StudentProjector, StudentTag>.MultiProjectorName;
        var actor = new GeneralMultiProjectionActor(domain, projectorName);

        // Create two student events
        var student1Id = Guid.NewGuid();
        var student2Id = Guid.NewGuid();
        
        var events = new[]
        {
            new Event(
                Payload: new StudentCreated(student1Id, "Alice", 5),
                SortableUniqueIdValue: SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-10), Guid.NewGuid()),
                EventType: "StudentCreated",
                Id: Guid.NewGuid(),
                EventMetadata: new EventMetadata(null, null, "test"),
                Tags: new List<string> { ((ITag)new StudentTag(student1Id)).GetTag() }
            ),
            new Event(
                Payload: new StudentCreated(student2Id, "Bob", 3),
                SortableUniqueIdValue: SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-5), Guid.NewGuid()),
                EventType: "StudentCreated",
                Id: Guid.NewGuid(),
                EventMetadata: new EventMetadata(null, null, "test"),
                Tags: new List<string> { ((ITag)new StudentTag(student2Id)).GetTag() }
            )
        };

        // Act - Add both events
        _output.WriteLine("Adding 2 student events...");
        await actor.AddEventsAsync(events);

        // Get state
        var stateResult = await actor.GetStateAsync();
        Assert.True(stateResult.IsSuccess);
        var state = stateResult.GetValue();

        // Assert
        if (state.Payload is GenericTagMultiProjector<StudentProjector, StudentTag> projector)
        {
            var payloads = projector.GetStatePayloads().ToList();
            
            _output.WriteLine($"State contains {payloads.Count} students");
            
            Assert.Equal(2, payloads.Count);
            
            var students = payloads.Cast<StudentState>().OrderBy(s => s.Name).ToList();
            Assert.Equal("Alice", students[0].Name);
            Assert.Equal("Bob", students[1].Name);
            
            _output.WriteLine($"✅ Both students found: {students[0].Name} and {students[1].Name}");
        }
        else
        {
            Assert.True(false, "State payload is not the expected type");
        }
    }
    
    [Fact]
    public async Task Adding_Students_One_By_One_Should_Show_Each_Immediately()
    {
        // Arrange
        var domain = DcbDomainTypes.Simple(builder =>
        {
            builder.EventTypes.RegisterEventType<StudentCreated>();
            builder.TagTypes.RegisterTagGroupType<StudentTag>();
            builder.TagProjectorTypes.RegisterProjector<StudentProjector>();
            builder.TagStatePayloadTypes.RegisterPayloadType<StudentState>();
            builder.MultiProjectorTypes.RegisterProjector<GenericTagMultiProjector<StudentProjector, StudentTag>>();
        });

        var projectorName = GenericTagMultiProjector<StudentProjector, StudentTag>.MultiProjectorName;
        var actor = new GeneralMultiProjectionActor(domain, projectorName);

        // Add first student
        var student1Id = Guid.NewGuid();
        var event1 = new Event(
            Payload: new StudentCreated(student1Id, "Alice", 5),
            SortableUniqueIdValue: SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
            EventType: "StudentCreated",
            Id: Guid.NewGuid(),
            EventMetadata: new EventMetadata(null, null, "test"),
            Tags: new List<string> { new StudentTag(student1Id).GetTag() }
        );

        _output.WriteLine("Adding first student (Alice)...");
        await actor.AddEventsAsync(new[] { event1 });
        
        // Check state after first student
        var state1 = await actor.GetStateAsync();
        Assert.True(state1.IsSuccess);
        
        if (state1.GetValue().Payload is GenericTagMultiProjector<StudentProjector, StudentTag> proj1)
        {
            var payloads1 = proj1.GetStatePayloads().ToList();
            _output.WriteLine($"After 1st student: {payloads1.Count} students in state");
            Assert.Single(payloads1);
            Assert.Equal("Alice", ((StudentState)payloads1[0]).Name);
        }

        // Add second student
        var student2Id = Guid.NewGuid();
        var event2 = new Event(
            Payload: new StudentCreated(student2Id, "Bob", 3),
            SortableUniqueIdValue: SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
            EventType: "StudentCreated",
            Id: Guid.NewGuid(),
            EventMetadata: new EventMetadata(null, null, "test"),
            Tags: new List<string> { new StudentTag(student2Id).GetTag() }
        );

        _output.WriteLine("Adding second student (Bob)...");
        await actor.AddEventsAsync(new[] { event2 });
        
        // Check state after second student
        var state2 = await actor.GetStateAsync();
        Assert.True(state2.IsSuccess);
        
        if (state2.GetValue().Payload is GenericTagMultiProjector<StudentProjector, StudentTag> proj2)
        {
            var payloads2 = proj2.GetStatePayloads().ToList();
            _output.WriteLine($"After 2nd student: {payloads2.Count} students in state");
            Assert.Equal(2, payloads2.Count);
            
            var students = payloads2.Cast<StudentState>().OrderBy(s => s.Name).ToList();
            Assert.Equal("Alice", students[0].Name);
            Assert.Equal("Bob", students[1].Name);
            
            _output.WriteLine($"✅ Both students now in state: {students[0].Name} and {students[1].Name}");
        }
    }
}