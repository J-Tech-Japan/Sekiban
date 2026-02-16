using System.Text;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Runtime.Native;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests;

public class NativeMultiProjectionProjectionPrimitiveTests
{
    [Fact]
    public void CreateAccumulator_ShouldReturnAccumulator()
    {
        var primitive = BuildPrimitive();

        var accumulator = primitive.CreateAccumulator(
            TestMultiProjector.MultiProjectorName,
            TestMultiProjector.MultiProjectorVersion);

        Assert.NotNull(accumulator);
    }

    [Fact]
    public void ApplySnapshot_ShouldSucceed_WhenSnapshotIsNull()
    {
        var primitive = BuildPrimitive();
        var accumulator = primitive.CreateAccumulator(
            TestMultiProjector.MultiProjectorName,
            TestMultiProjector.MultiProjectorVersion);

        var result = accumulator.ApplySnapshot(null);

        Assert.True(result);
    }

    [Fact]
    public void ApplyEvents_ShouldBuildState_FromScratch()
    {
        var domain = BuildDomain();
        var primitive = new NativeMultiProjectionProjectionPrimitive(domain);
        var accumulator = primitive.CreateAccumulator(
            TestMultiProjector.MultiProjectorName,
            TestMultiProjector.MultiProjectorVersion);

        accumulator.ApplySnapshot(null);

        var events = BuildSerializableEvents(domain, 3);
        var applied = accumulator.ApplyEvents(events, events.Last().SortableUniqueIdValue);

        Assert.True(applied);

        var snapshotResult = accumulator.GetSnapshot();
        Assert.True(snapshotResult.IsSuccess);
        var envelope = snapshotResult.GetValue();
        Assert.NotNull(envelope.InlineState);
        Assert.Equal(TestMultiProjector.MultiProjectorName, envelope.InlineState!.ProjectorName);
    }

    [Fact]
    public void GetMetadata_ShouldReturnCorrectMetadata_AfterEvents()
    {
        var domain = BuildDomain();
        var primitive = new NativeMultiProjectionProjectionPrimitive(domain);
        var accumulator = primitive.CreateAccumulator(
            TestMultiProjector.MultiProjectorName,
            TestMultiProjector.MultiProjectorVersion);

        accumulator.ApplySnapshot(null);

        var events = BuildSerializableEvents(domain, 2);
        accumulator.ApplyEvents(events, events.Last().SortableUniqueIdValue);

        var metadataResult = accumulator.GetMetadata();
        Assert.True(metadataResult.IsSuccess);
        var metadata = metadataResult.GetValue();

        Assert.Equal(TestMultiProjector.MultiProjectorName, metadata.ProjectorName);
        Assert.Equal(TestMultiProjector.MultiProjectorVersion, metadata.ProjectorVersion);
    }

    [Fact]
    public void ApplyEvents_ShouldReturnTrue_WhenNoEvents()
    {
        var primitive = BuildPrimitive();
        var accumulator = primitive.CreateAccumulator(
            TestMultiProjector.MultiProjectorName,
            TestMultiProjector.MultiProjectorVersion);
        accumulator.ApplySnapshot(null);

        var applied = accumulator.ApplyEvents(
            Array.Empty<SerializableEvent>(),
            null);

        Assert.True(applied);
    }

    [Fact]
    public void GetSnapshot_ShouldReturnSnapshot_AfterApplyingSnapshotAndEvents()
    {
        var domain = BuildDomain();
        var primitive = new NativeMultiProjectionProjectionPrimitive(domain);

        // Build initial state
        var accumulator1 = primitive.CreateAccumulator(
            TestMultiProjector.MultiProjectorName,
            TestMultiProjector.MultiProjectorVersion);
        accumulator1.ApplySnapshot(null);
        var events1 = BuildSerializableEvents(domain, 2);
        accumulator1.ApplyEvents(events1, events1.Last().SortableUniqueIdValue);
        var snapshot1 = accumulator1.GetSnapshot();
        Assert.True(snapshot1.IsSuccess);

        // Restore from snapshot and apply more events
        var accumulator2 = primitive.CreateAccumulator(
            TestMultiProjector.MultiProjectorName,
            TestMultiProjector.MultiProjectorVersion);
        var restored = accumulator2.ApplySnapshot(snapshot1.GetValue());
        Assert.True(restored);

        var events2 = BuildSerializableEvents(domain, 1);
        accumulator2.ApplyEvents(events2, events2.Last().SortableUniqueIdValue);

        var snapshot2 = accumulator2.GetSnapshot();
        Assert.True(snapshot2.IsSuccess);
        Assert.NotNull(snapshot2.GetValue().InlineState);
    }

    [Fact]
    public void ApplySnapshot_ShouldReturnFalse_WhenSnapshotIsOffloaded()
    {
        var primitive = BuildPrimitive();
        var accumulator = primitive.CreateAccumulator(
            TestMultiProjector.MultiProjectorName,
            TestMultiProjector.MultiProjectorVersion);

        var offloadedEnvelope = new SerializableMultiProjectionStateEnvelope(
            IsOffloaded: true,
            InlineState: null,
            OffloadedState: null);

        var result = accumulator.ApplySnapshot(offloadedEnvelope);

        Assert.False(result);
    }

    [Fact]
    public void ApplyEvents_ShouldReturnFalse_WhenEventTypeIsUnregistered()
    {
        var primitive = BuildPrimitive();
        var accumulator = primitive.CreateAccumulator(
            TestMultiProjector.MultiProjectorName,
            TestMultiProjector.MultiProjectorVersion);
        accumulator.ApplySnapshot(null);

        var sortable = SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid());
        var unregisteredEvent = new SerializableEvent(
            Payload: Encoding.UTF8.GetBytes("{\"Name\":\"test\"}"),
            SortableUniqueIdValue: sortable,
            Id: Guid.NewGuid(),
            EventMetadata: new EventMetadata(
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                "test"),
            Tags: new List<string>(),
            EventPayloadName: "UnregisteredEventType");

        var result = accumulator.ApplyEvents(
            new List<SerializableEvent> { unregisteredEvent },
            sortable);

        Assert.False(result);
    }

    private static NativeMultiProjectionProjectionPrimitive BuildPrimitive()
    {
        return new NativeMultiProjectionProjectionPrimitive(BuildDomain());
    }

    private static DcbDomainTypes BuildDomain()
    {
        return DcbDomainTypesExtensions.Simple(builder =>
        {
            builder.EventTypes.RegisterEventType<ItemAdded>();
            builder.MultiProjectorTypes.RegisterProjector<TestMultiProjector>();
        });
    }

    private static List<SerializableEvent> BuildSerializableEvents(DcbDomainTypes domain, int count)
    {
        var events = new List<SerializableEvent>();
        for (var i = 0; i < count; i++)
        {
            var sortable = SortableUniqueId.Generate(
                DateTime.UtcNow.AddSeconds(-60 + i), Guid.NewGuid());
            var ev = new Event(
                new ItemAdded($"item-{i}"),
                sortable,
                nameof(ItemAdded),
                Guid.CreateVersion7(),
                new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
                new List<string>());
            events.Add(ev.ToSerializableEvent(domain.EventTypes));
        }
        return events;
    }

    public record ItemAdded(string Name) : IEventPayload;

    public record TestMultiProjector(int Count) : IMultiProjector<TestMultiProjector>
    {
        public TestMultiProjector() : this(0) { }

        public static string MultiProjectorName => "TestMultiProjector";
        public static string MultiProjectorVersion => "1.0";

        public static ResultBox<TestMultiProjector> Project(
            TestMultiProjector payload,
            Event ev,
            List<ITag> tags,
            DcbDomainTypes domainTypes,
            SortableUniqueId safeWindowThreshold) =>
            ResultBox.FromValue(payload with { Count = payload.Count + 1 });

        public static TestMultiProjector GenerateInitialPayload() => new(0);
    }
}
