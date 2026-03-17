using Dcb.Domain.Student;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Tests;

public class GeneralMultiProjectionActorSnapshotValidationTests
{
    private readonly DcbDomainTypes _domainTypes;

    public GeneralMultiProjectionActorSnapshotValidationTests()
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
    public async Task SetSnapshotAsync_Rejects_EmptyCustomTagSnapshot_WithCheckpointAndVersion()
    {
        var actor = new GeneralMultiProjectionActor(
            _domainTypes,
            GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.MultiProjectorName);

        var checkpoint = SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid());
        var state = SerializableMultiProjectionState.FromBytes(
            GzipCompression.CompressString("{\"v\":1,\"items\":[]}"),
            typeof(GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>).FullName ?? "payload",
            GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.MultiProjectorName,
            GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.MultiProjectorVersion,
            checkpoint,
            Guid.Empty,
            version: 1,
            isCatchedUp: true,
            isSafeState: true);

        var envelope = new SerializableMultiProjectionStateEnvelope(false, state, null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => actor.SetSnapshotAsync(envelope));
        Assert.Contains("integrity check failed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetSnapshotAsync_Allows_EmptyCustomTagSnapshot_WithoutProgress()
    {
        var actor = new GeneralMultiProjectionActor(
            _domainTypes,
            GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.MultiProjectorName);

        var state = SerializableMultiProjectionState.FromBytes(
            GzipCompression.CompressString("{\"v\":1,\"items\":[]}"),
            typeof(GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>).FullName ?? "payload",
            GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.MultiProjectorName,
            GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>.MultiProjectorVersion,
            string.Empty,
            Guid.Empty,
            version: 0,
            isCatchedUp: true,
            isSafeState: true);

        await actor.SetSnapshotAsync(new SerializableMultiProjectionStateEnvelope(false, state, null));

        var restoredState = await actor.GetStateAsync(canGetUnsafeState: false);
        Assert.True(restoredState.IsSuccess);
        var projector = Assert.IsType<GenericStringTagMultiProjector<StudentProjector, StudentCodeTag>>(
            restoredState.GetValue().Payload);
        Assert.Empty(projector.GetCurrentTagStates());
    }
}
