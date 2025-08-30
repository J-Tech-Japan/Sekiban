using Dcb.Domain.Enrollment;
using Dcb.Domain.Student;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
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
            builder.MultiProjectorTypes.RegisterProjector<StudentSummaries>();
        });
    }

    private static Event MakeEvent<TPayload>(TPayload payload) where TPayload : IEventPayload =>
        new(
            payload,
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
            typeof(TPayload).FullName!,
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), "test"),
            new List<string>());

    [Fact]
    public async Task Actor_Applies_Events_And_Serializes_State()
    {
        var domain = CreateDomain();
        var actor = new GeneralMultiProjectionActor(domain, StudentSummaries.MultiProjectorName);

        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();
        await actor.AddEventsAsync(new[] { MakeEvent(new StudentCreated(s1, "Taro", 2)) });
        await actor.AddEventsAsync(new[] { MakeEvent(new StudentEnrolledInClassRoom(s1, Guid.NewGuid())) });
        await actor.AddEventsAsync(new[] { MakeEvent(new StudentEnrolledInClassRoom(s1, Guid.NewGuid())) });
        await actor.AddEventsAsync(new[] { MakeEvent(new StudentDroppedFromClassRoom(s1, Guid.NewGuid())) });
        await actor.AddEventsAsync(new[] { MakeEvent(new StudentCreated(s2, "Hanako", 1)) });
        await actor.AddEventsAsync(new[] { MakeEvent(new StudentEnrolledInClassRoom(s2, Guid.NewGuid())) });
        var envRb = await actor.GetSnapshotAsync();
        Assert.True(envRb.IsSuccess);
        var env = envRb.GetValue();
        Assert.False(env.IsOffloaded);
        var state = env.InlineState!;
        Assert.Equal(StudentSummaries.MultiProjectorName, state.ProjectorName);
        Assert.Equal("1.0.0", state.ProjectorVersion);
        Assert.Equal(6, state.Version);

        var projectorRb = domain.MultiProjectorTypes.Deserialize(
            state.Payload,
            state.ProjectorName,
            domain.JsonSerializerOptions);
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
        var actor = new GeneralMultiProjectionActor(domain, StudentSummaries.MultiProjectorName);

        var s1 = Guid.NewGuid();
        await actor.AddEventsAsync(new[] { MakeEvent(new StudentCreated(s1, "Hanako", 1)) });
        await actor.AddEventsAsync(new[] { MakeEvent(new StudentEnrolledInClassRoom(s1, Guid.NewGuid())) });
        var savedEnvRb = await actor.GetSnapshotAsync();
        Assert.True(savedEnvRb.IsSuccess);
        var savedEnv = savedEnvRb.GetValue();
        Assert.False(savedEnv.IsOffloaded);
        var saved = savedEnv.InlineState!;

        var actor2 = new GeneralMultiProjectionActor(domain, StudentSummaries.MultiProjectorName);
        await actor2.SetSnapshotAsync(new Sekiban.Dcb.Snapshots.SerializableMultiProjectionStateEnvelope(false, saved, null));

        await actor2.AddEventsAsync(new[] { MakeEvent(new StudentDroppedFromClassRoom(s1, Guid.NewGuid())) });
        var envRb2 = await actor2.GetSnapshotAsync();
        Assert.True(envRb2.IsSuccess);
        var env2 = envRb2.GetValue();
        Assert.False(env2.IsOffloaded);
        var state2 = env2.InlineState!;
        Assert.Equal("1.0.0", state2.ProjectorVersion);
        Assert.Equal(3, state2.Version);

        var projectorRb2 = domain.MultiProjectorTypes.Deserialize(
            state2.Payload,
            state2.ProjectorName,
            domain.JsonSerializerOptions);
        Assert.True(projectorRb2.IsSuccess);
        var projector2 = Assert.IsType<StudentSummaries>(projectorRb2.GetValue());
        Assert.True(projector2.Students.TryGetValue(s1, out var s1Item2));
        Assert.Equal("Hanako", s1Item2.Name);
        Assert.Equal(0, s1Item2.EnrolledCount);
    }
}
