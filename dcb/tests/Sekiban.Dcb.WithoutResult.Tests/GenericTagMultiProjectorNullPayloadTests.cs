using Dcb.Domain.WithoutResult.ClassRoom;
using Dcb.Domain.WithoutResult.Student;
using Sekiban.Dcb.Boundaries;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.WithoutResult.Tests;

/// <summary>
///     A null payload on the path that skips the event (no tag of the projector's tag group) used to come back from
///     10.x's internal box as a failure, thrown with the projector's boundary. It must not turn into a silent null.
/// </summary>
public class GenericTagMultiProjectorNullPayloadTests
{
    private static readonly SortableUniqueId Threshold = SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-20), Guid.Empty);

    private static Event UnrelatedEvent() =>
        new(
            new StudentCreated(Guid.NewGuid(), "Alice"),
            SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid()),
            nameof(StudentCreated),
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "TestUser"),
            []);

    private static List<ITag> UnrelatedTags() => [new ClassRoomTag(Guid.NewGuid())];

    private static void AssertNoPayloadFailure(Exception thrown, string projectorName)
    {
        var failure = Assert.IsType<InvalidOperationException>(thrown);
        Assert.Equal(
            $"{projectorName}.Project produced no payload: the payload passed in was null and the event carried no " +
            "tag of the projector's tag group.",
            failure.Message);
        Assert.Equal($"{projectorName}.Project", failure.Data[BoundaryGuard.OperationDataKey]);
        Assert.Equal(nameof(StudentCreated), failure.Data[BoundaryGuard.TargetDataKey]);
    }

    [Fact]
    public void GuidTagProjector_NullPayloadWithoutItsTag_Throws()
    {
        var thrown = Record.Exception(() => GenericTagMultiProjector<StudentProjector, StudentTag>.Project(
            null!, UnrelatedEvent(), UnrelatedTags(), null!, Threshold));
        AssertNoPayloadFailure(thrown!, "GenericTagMultiProjector");
    }

    [Fact]
    public void IntTagProjector_NullPayloadWithoutItsTag_Throws()
    {
        var thrown = Record.Exception(() => GenericIntTagMultiProjector<StudentProjector, YearlyStudentsTag>.Project(
            null!, UnrelatedEvent(), UnrelatedTags(), null!, Threshold));
        AssertNoPayloadFailure(thrown!, "GenericIntTagMultiProjector");
    }

    [Fact]
    public void StringTagProjector_NullPayloadWithoutItsTag_Throws()
    {
        var thrown = Record.Exception(() => GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.Project(
            null!, UnrelatedEvent(), UnrelatedTags(), null!, Threshold));
        AssertNoPayloadFailure(thrown!, "GenericStringTagMultiProjector");
    }

    [Fact]
    public void NonNullPayloadWithoutItsTag_IsReturnedUnchanged()
    {
        var payload = GenericTagMultiProjector<StudentProjector, StudentTag>.GenerateInitialPayload();
        var projected = GenericTagMultiProjector<StudentProjector, StudentTag>.Project(
            payload, UnrelatedEvent(), UnrelatedTags(), null!, Threshold);
        Assert.Same(payload, projected);
    }
}
