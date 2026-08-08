using System.Text;
using System.Text.Json;
using Dcb.Domain;
using Dcb.Domain.Student;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;
using CoreInMemoryMultiProjectionStateStore = Sekiban.Dcb.Testing.InMemoryMultiProjectionStateStore;

namespace Sekiban.Dcb.Tests;

public sealed class ProjectionStatusRegistryTests
{
    [Fact]
    public async Task Reader_UsesLastTraversedCursor_AndKeepsAppliedCountDistinct()
    {
        var provider = new MutableServiceIdProvider("alpha");
        var domainTypes = DomainType.GetDomainTypes();
        var eventStore = new CoreInMemoryEventStore(domainTypes.EventTypes, provider);
        var statusStore = new CoreInMemoryMultiProjectionStateStore(provider);
        var events = new[]
        {
            EventTestHelper.CreateEvent(new StudentCreated(Guid.NewGuid(), "Alice"),
                new StudentTag(Guid.NewGuid())),
            EventTestHelper.CreateEvent(new StudentCreated(Guid.NewGuid(), "Bob"),
                new StudentTag(Guid.NewGuid()))
        }.OrderBy(item => item.SortableUniqueIdValue).ToArray();

        var write = await eventStore.WriteEventsAsync(events, domainTypes.EventTypes);
        Assert.True(write.IsSuccess);

        var heartbeat = new ProjectionStatusHeartbeat(
            "alpha",
            "students",
            "v1",
            "cluster-a",
            "activation-a",
            1,
            1,
            events[0].SortableUniqueIdValue,
            events[1].SortableUniqueIdValue,
            DateTimeOffset.UtcNow);
        var heartbeatWrite = await statusStore.UpsertAsync(heartbeat, 0);
        Assert.True(heartbeatWrite.IsSuccess);

        var reader = new ProjectionStatusReader(statusStore, eventStore, provider);
        var result = await reader.ReadAsync(new ProjectionStatusReadRequest(ProjectorName: "students"));

        Assert.True(result.IsSuccess);
        var snapshot = Assert.Single(result.GetValue());
        Assert.Equal(2, snapshot.TotalEventCount);
        Assert.Equal(0, snapshot.RemainingEventCount);
        Assert.Equal(1, snapshot.AppliedEventCount);
        Assert.Equal(events[1].SortableUniqueIdValue, snapshot.LastTraversedSortableUniqueId);
        Assert.True(snapshot.IsCaughtUp);
        Assert.Equal(ProjectionStatusSnapshot.BestEffortConsistency, snapshot.Consistency);
    }

    [Fact]
    public async Task Registry_CasRejectsStaleWriter_AndReaderSurfacesFreshActivationConflict()
    {
        var provider = new MutableServiceIdProvider("alpha");
        var statusStore = new CoreInMemoryMultiProjectionStateStore(provider);
        var now = DateTimeOffset.UtcNow;
        var first = CreateHeartbeat("activation-a", 1, now);
        var second = CreateHeartbeat("activation-b", 1, now);

        var committed = await statusStore.UpsertAsync(first, 0);
        var stale = await statusStore.UpsertAsync(first, 0);
        var secondCommitted = await statusStore.UpsertAsync(second, 0);

        Assert.True(committed.IsSuccess);
        Assert.True(committed.GetValue().Committed);
        Assert.True(stale.IsSuccess);
        Assert.True(stale.GetValue().Conflict);
        Assert.Equal(1, stale.GetValue().Current!.Sequence);
        Assert.True(secondCommitted.IsSuccess);

        var eventStore = new CoreInMemoryEventStore(DomainType.GetDomainTypes().EventTypes, provider);
        var reader = new ProjectionStatusReader(
            statusStore,
            eventStore,
            provider,
            new ProjectionStatusOptions { FreshnessWindow = TimeSpan.FromMinutes(5) });
        var result = await reader.ReadAsync();

        Assert.True(result.IsSuccess);
        var snapshots = result.GetValue();
        Assert.Equal(2, snapshots.Count);
        Assert.All(snapshots, snapshot =>
        {
            Assert.True(snapshot.HasConflict);
            Assert.Equal(new[] { "activation-a", "activation-b" }, snapshot.ConflictActivations);
        });
    }

    [Fact]
    public async Task SerializedReader_EmitsV1Envelope_AndRejectsUnknownVersionBeforeBinding()
    {
        var provider = new MutableServiceIdProvider("server-service");
        var eventStore = new CoreInMemoryEventStore(DomainType.GetDomainTypes().EventTypes, provider);
        var statusStore = new CoreInMemoryMultiProjectionStateStore(provider);
        var reader = new SerializedProjectionStatusReader(
            new ProjectionStatusReader(statusStore, eventStore, provider),
            provider);

        var serialized = await reader.ReadSerializedAsync();
        Assert.True(serialized.IsSuccess);
        using var document = JsonDocument.Parse(serialized.GetValue());
        Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("server-service", document.RootElement.GetProperty("serviceId").GetString());

        var roundTrip = SerializedProjectionStatusReader.Deserialize(serialized.GetValue());
        Assert.True(roundTrip.IsSuccess);
        Assert.Equal("server-service", roundTrip.GetValue().ServiceId);

        var unsupported = SerializedProjectionStatusReader.Deserialize(
            Encoding.UTF8.GetBytes("{\"version\":99,\"serviceId\":\"client\",\"snapshots\":[]}"));
        Assert.False(unsupported.IsSuccess);
        Assert.IsType<UnsupportedSerializedProjectionStatusVersionException>(unsupported.GetException());
    }

    [Fact]
    public async Task SqliteStatusStore_AutoCreatesTable_AndRejectsStaleSequence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sekiban-projection-status-{Guid.NewGuid():N}.db");
        try
        {
            var provider = new MutableServiceIdProvider("alpha");
            var store = new SqliteMultiProjectionStateStore(path, serviceIdProvider: provider);
            var heartbeat = CreateHeartbeat("activation-a", 1, DateTimeOffset.UtcNow);

            var committed = await store.UpsertAsync(heartbeat, 0);
            var stale = await store.UpsertAsync(heartbeat with { Sequence = 2 }, 0);
            var rows = await store.ListAsync("students", "v1");

            Assert.True(committed.IsSuccess);
            Assert.True(committed.GetValue().Committed);
            Assert.True(stale.IsSuccess);
            Assert.True(stale.GetValue().Conflict);
            Assert.True(rows.IsSuccess);
            Assert.Single(rows.GetValue());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static ProjectionStatusHeartbeat CreateHeartbeat(
        string activationId,
        long sequence,
        DateTimeOffset recordedAtUtc) =>
        new(
            "alpha",
            "students",
            "v1",
            "cluster-a",
            activationId,
            sequence,
            0,
            null,
            null,
            recordedAtUtc);

    private sealed class MutableServiceIdProvider : IServiceIdProvider
    {
        private readonly string _serviceId;

        public MutableServiceIdProvider(string serviceId) => _serviceId = serviceId;

        public string GetCurrentServiceId() => ServiceIdValidator.NormalizeAndValidate(_serviceId);
    }
}
