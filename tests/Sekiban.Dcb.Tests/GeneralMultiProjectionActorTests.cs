using System;
using System.Threading.Tasks;
using Dcb.Domain;
using Dcb.Domain.Enrollment;
using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Xunit;

namespace Sekiban.Dcb.Tests;

public class GeneralMultiProjectionActorTests
{
    private static DcbDomainTypes CreateDomain()
    {
        return DcbDomainTypes.Simple(builder =>
        {
            builder.EventTypes.RegisterEventType<StudentCreated>();
            builder.EventTypes.RegisterEventType<StudentEnrolledInClassRoom>();
            builder.EventTypes.RegisterEventType<StudentDroppedFromClassRoom>();
            builder.MultiProjectorTypes.RegisterProjector<StudentSummaries, StudentSummaries>();
        });
    }

    private static Event MakeEvent<TPayload>(TPayload payload) where TPayload : IEventPayload =>
        new(
            payload,
            SortableUniqueIdValue: Guid.NewGuid().ToString("N"),
            EventType: typeof(TPayload).FullName!,
            Id: Guid.NewGuid(),
            EventMetadata: new EventMetadata(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "test"),
            Tags: new System.Collections.Generic.List<string>());

    [Fact]
    public async Task Actor_Applies_Events_And_Serializes_State()
    {
    var domain = CreateDomain();
    var actor = new GeneralMultiProjectionActor(domain, StudentSummaries.GetMultiProjectorName());

    var s1 = Guid.NewGuid();
    var s2 = Guid.NewGuid();
    await actor.AddEventsAsync(new[] { MakeEvent(new StudentCreated(s1, "Taro", 2)) });
    await actor.AddEventsAsync(new[] { MakeEvent(new StudentEnrolledInClassRoom(s1, Guid.NewGuid())) });
    await actor.AddEventsAsync(new[] { MakeEvent(new StudentEnrolledInClassRoom(s1, Guid.NewGuid())) });
    await actor.AddEventsAsync(new[] { MakeEvent(new StudentDroppedFromClassRoom(s1, Guid.NewGuid())) });
    await actor.AddEventsAsync(new[] { MakeEvent(new StudentCreated(s2, "Hanako", 1)) });
    await actor.AddEventsAsync(new[] { MakeEvent(new StudentEnrolledInClassRoom(s2, Guid.NewGuid())) });
        var stateRb = await actor.GetSerializableStateAsync();
        Assert.True(stateRb.IsSuccess);
        var state = stateRb.GetValue();
        Assert.Equal(StudentSummaries.GetMultiProjectorName(), state.ProjectorName);
        Assert.Equal("1.0.0", state.ProjectorVersion);
        Assert.Equal(6, state.Version);

    var projectorRb = domain.MultiProjectorTypes.Deserialize(state.Payload, state.MultiProjectionPayloadType, domain.JsonSerializerOptions);
    Assert.True(projectorRb.IsSuccess);
    var projector = Assert.IsType<StudentSummaries>(projectorRb.GetValue());
    Assert.True(projector.Students.TryGetValue(s1, out var s1Item));
    Assert.Equal("Taro", s1Item.Name);
    Assert.Equal(1, s1Item.EnrolledCount);
    Assert.True(projector.Students.TryGetValue(s2, out var s2Item));
    Assert.Equal("Hanako", s2Item.Name);
    Assert.Equal(1, s2Item.EnrolledCount);
    }

    [Fact]
    public async Task Actor_Restores_From_Serialized_State()
    {
    var domain = CreateDomain();
    var actor = new GeneralMultiProjectionActor(domain, StudentSummaries.GetMultiProjectorName());

    var s1 = Guid.NewGuid();
    await actor.AddEventsAsync(new[] { MakeEvent(new StudentCreated(s1, "Hanako", 1)) });
    await actor.AddEventsAsync(new[] { MakeEvent(new StudentEnrolledInClassRoom(s1, Guid.NewGuid())) });
        var savedRb = await actor.GetSerializableStateAsync();
        Assert.True(savedRb.IsSuccess);
        var saved = savedRb.GetValue();

    var actor2 = new GeneralMultiProjectionActor(domain, StudentSummaries.GetMultiProjectorName());
        await actor2.SetCurrentState(saved);

    await actor2.AddEventsAsync(new[] { MakeEvent(new StudentDroppedFromClassRoom(s1, Guid.NewGuid())) });
        var stateRb2 = await actor2.GetSerializableStateAsync();
        Assert.True(stateRb2.IsSuccess);
        var state2 = stateRb2.GetValue();
        Assert.Equal("1.0.0", state2.ProjectorVersion);
        Assert.Equal(3, state2.Version);

    var projectorRb2 = domain.MultiProjectorTypes.Deserialize(state2.Payload, state2.MultiProjectionPayloadType, domain.JsonSerializerOptions);
    Assert.True(projectorRb2.IsSuccess);
    var projector2 = Assert.IsType<StudentSummaries>(projectorRb2.GetValue());
    Assert.True(projector2.Students.TryGetValue(s1, out var s1Item2));
    Assert.Equal("Hanako", s1Item2.Name);
    Assert.Equal(0, s1Item2.EnrolledCount);
    }
}
