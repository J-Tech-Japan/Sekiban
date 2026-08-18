using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using Microsoft.Extensions.Options;
using Dcb.Domain;
using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Storage.Checkpoints;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Tests.Cosmos;
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
            DateTimeOffset.UtcNow)
        {
            Phase = ProjectionStatusPhases.Active
        };
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
        var second = CreateHeartbeat("activation-c", 1, now) with { ClusterId = "cluster-b" };

        var committed = await statusStore.UpsertAsync(first, 0);
        var stale = await statusStore.UpsertAsync(first, 0);
        var replacement = await statusStore.UpsertAsync(
            first with { ActivationId = "activation-b", Sequence = 2 },
            1);
        var staleReplacement = await statusStore.UpsertAsync(
            first with { ActivationId = "activation-a", Sequence = 3 },
            1);
        var secondCommitted = await statusStore.UpsertAsync(second, 0);

        Assert.True(committed.IsSuccess);
        Assert.True(committed.GetValue().Committed);
        Assert.True(stale.IsSuccess);
        Assert.True(stale.GetValue().Conflict);
        Assert.Equal(1, stale.GetValue().Current!.Sequence);
        Assert.True(replacement.IsSuccess);
        Assert.True(replacement.GetValue().Committed);
        Assert.Equal("activation-b", replacement.GetValue().Current!.ActivationId);
        Assert.True(staleReplacement.IsSuccess);
        Assert.True(staleReplacement.GetValue().Conflict);
        Assert.Equal("activation-b", staleReplacement.GetValue().Current!.ActivationId);
        Assert.True(secondCommitted.IsSuccess);

        var eventStore = new CoreInMemoryEventStore(DomainType.GetDomainTypes().EventTypes, provider);
        var reader = new ProjectionStatusReader(
            statusStore,
            eventStore,
            provider,
            new ProjectionStatusOptions { FreshnessWindow = TimeSpan.FromMinutes(5) });
        var result = await reader.ReadAsync(new ProjectionStatusReadRequest
        {
            ExpectedProjectorVersion = "v1"
        });

        Assert.True(result.IsSuccess);
        var snapshots = result.GetValue();
        Assert.Equal(2, snapshots.Count);
        Assert.All(snapshots, snapshot =>
        {
            Assert.True(snapshot.HasConflict);
            Assert.True(snapshot.IsFresh);
            Assert.Equal(ProjectionStatusVersionMatch.Match, snapshot.VersionMatch);
            Assert.Equal(new[] { "activation-b", "activation-c" }, snapshot.ConflictActivations);
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
    public async Task SerializedRequestGate_RejectsBeforeAnyStoreRead_AndSeparatesVersionAndShapeErrors()
    {
        var provider = new MutableServiceIdProvider("server-service");
        var eventStore = new CountingEventStore(provider);
        var stateStore = new CoreInMemoryMultiProjectionStateStore(provider);
        var statusStore = new CountingStatusStore(stateStore);
        var serialized = new SerializedProjectionStatusReader(
            new ProjectionStatusReader(
                statusStore,
                eventStore,
                provider,
                new ProjectionStatusOptions { SamplingWindow = TimeSpan.FromMinutes(1) }),
            provider);

        var rejectedInputs = new[]
        {
            "{}", // missing version
            "{\"version\":\"1\"}", // wrong-typed version
            "{\"version\":1,\"unknown\":true}", // unknown member
            "{\"version\":1,\"projectorName\":42}", // wrong-typed filter
            "not-json" // malformed JSON
        };

        foreach (var input in rejectedInputs)
        {
            var result = await serialized.AcceptAsync(Encoding.UTF8.GetBytes(input));
            Assert.False(result.IsSuccess);
            Assert.IsType<SerializedProjectionStatusShapeException>(result.GetException());
        }

        var unsupported = await serialized.AcceptAsync(
            Encoding.UTF8.GetBytes("{\"version\":99,\"unknown\":{\"malformed\":true}}"));
        Assert.False(unsupported.IsSuccess);
        Assert.IsType<UnsupportedSerializedProjectionStatusVersionException>(unsupported.GetException());
        Assert.Equal(0, statusStore.ListCalls);
        Assert.Equal(0, eventStore.TotalCountCalls);
        Assert.Equal(0, eventStore.CursorCountCalls);
    }

    [Fact]
    public async Task SerializedRequestAndResponse_UseFrozenV1GoldenVectors()
    {
        var request = new ProjectionStatusReadRequest("server-service", "students", "v1");
        Assert.Equal(
            "{\"version\":1,\"serviceId\":\"server-service\",\"projectorName\":\"students\",\"projectorVersion\":\"v1\"}",
            Encoding.UTF8.GetString(SerializedProjectionStatusReader.SerializeRequest(request)));

        var snapshot = new ProjectionStatusSnapshot(
            "students",
            "v1",
            "cluster-a",
            "activation-a",
            7,
            3,
            "0001",
            "0002",
            4,
            0,
            DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"),
            ProjectionStatusSnapshot.BestEffortConsistency,
            true,
            false,
            Array.Empty<string>())
        {
            Phase = ProjectionStatusPhases.Active,
            LeaseExpiresAtUtc = DateTimeOffset.Parse("2026-01-01T00:01:00+00:00"),
            IsFaulted = false,
            FaultMessage = null,
            IsFresh = true,
            RecordedAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:30+00:00"),
            VersionMatch = ProjectionStatusVersionMatch.Match
        };
        var adapter = new SerializedProjectionStatusReader(new FixedStatusReader(snapshot), new MutableServiceIdProvider("server-service"));
        var response = await adapter.ReadSerializedAsync();

        Assert.True(response.IsSuccess);
        Assert.Equal(
            "{\"version\":1,\"serviceId\":\"server-service\",\"snapshots\":[{\"projectorName\":\"students\",\"projectorVersion\":\"v1\",\"clusterId\":\"cluster-a\",\"activationId\":\"activation-a\",\"sequence\":7,\"appliedEventCount\":3,\"lastAppliedSortableUniqueId\":\"0001\",\"lastTraversedSortableUniqueId\":\"0002\",\"totalEventCount\":4,\"remainingEventCount\":0,\"sampledAtUtc\":\"2026-01-01T00:00:00+00:00\",\"consistency\":\"bestEffort\",\"isCaughtUp\":true,\"hasConflict\":false,\"conflictingActivationIds\":[],\"phase\":\"active\",\"leaseExpiresAtUtc\":\"2026-01-01T00:01:00+00:00\",\"isFaulted\":false,\"faultMessage\":null,\"isFresh\":true}]}",
            Encoding.UTF8.GetString(response.GetValue()));
    }

    [Fact]
    public async Task Reader_ExposesRollingVersionsInV2WhileLeavingV1VectorsFrozen()
    {
        var provider = new MutableServiceIdProvider("alpha");
        var statusStore = new CoreInMemoryMultiProjectionStateStore(provider);
        var eventStore = new CoreInMemoryEventStore(DomainType.GetDomainTypes().EventTypes, provider);
        var now = DateTimeOffset.UtcNow;

        await statusStore.UpsertAsync(CreateHeartbeat("fresh-v1", 1, now) with
        {
            ProjectorVersion = "v1",
            Phase = ProjectionStatusPhases.Active,
            LeaseExpiresAtUtc = now.AddMinutes(1)
        }, 0);
        await statusStore.UpsertAsync(CreateHeartbeat("fresh-v2", 1, now) with
        {
            ProjectorVersion = "v2",
            Phase = ProjectionStatusPhases.Active,
            LeaseExpiresAtUtc = now.AddMinutes(1)
        }, 0);
        await statusStore.UpsertAsync(CreateHeartbeat("expired-v0", 1, now.AddMinutes(-10)) with
        {
            ProjectorVersion = "v0",
            Phase = ProjectionStatusPhases.Active,
            LeaseExpiresAtUtc = now.AddMinutes(-1)
        }, 0);

        var request = new ProjectionStatusReadRequest(ProjectorName: "students")
        {
            ExpectedProjectorVersion = "v2"
        };
        var reader = new ProjectionStatusReader(
            statusStore,
            eventStore,
            provider,
            new ProjectionStatusOptions { FreshnessWindow = TimeSpan.FromMinutes(2), SamplingWindow = TimeSpan.Zero });
        var snapshots = (await reader.ReadAsync(request)).GetValue();

        var current = Assert.Single(snapshots, snapshot => snapshot.ProjectorVersion == "v2");
        Assert.Equal("v2", current.ExpectedProjectorVersion);
        Assert.Equal("v2", current.ObservedProjectorVersion);
        Assert.Equal(now, current.RecordedAtUtc);
        Assert.Equal(ProjectionStatusVersionMatch.Match, current.VersionMatch);
        Assert.Equal(ProjectionStatusVersionDisposition.Current, current.VersionDisposition);

        var freshDifferentVersion = Assert.Single(snapshots, snapshot => snapshot.ProjectorVersion == "v1");
        Assert.True(freshDifferentVersion.IsFresh);
        Assert.Equal(ProjectionStatusVersionMatch.Mismatch, freshDifferentVersion.VersionMatch);
        Assert.Equal(ProjectionStatusVersionDisposition.VersionMismatch, freshDifferentVersion.VersionDisposition);
        Assert.False(freshDifferentVersion.IsStaleOrOrphan);

        var expiredOrphan = Assert.Single(snapshots, snapshot => snapshot.ProjectorVersion == "v0");
        Assert.False(expiredOrphan.IsFresh);
        Assert.Equal(ProjectionStatusVersionMatch.Mismatch, expiredOrphan.VersionMatch);
        Assert.Equal(ProjectionStatusVersionDisposition.StaleOrOrphan, expiredOrphan.VersionDisposition);
        Assert.True(expiredOrphan.IsStaleOrOrphan);
        Assert.False(expiredOrphan.IsCaughtUp);

        var serialized = new SerializedProjectionStatusReader(reader, provider);
        var v2 = await serialized.AcceptAsync(SerializedProjectionStatusReader.SerializeRequestV2(request));
        Assert.True(v2.IsSuccess);
        var envelope = SerializedProjectionStatusReader.DeserializeV2(v2.GetValue());
        Assert.True(envelope.IsSuccess);
        Assert.Equal(2, envelope.GetValue().Version);
        var serializedCurrent = Assert.Single(
            envelope.GetValue().Snapshots,
            snapshot => snapshot.ObservedProjectorVersion == "v2");
        Assert.Equal(now, serializedCurrent.RecordedAtUtc);
        Assert.Equal(ProjectionStatusVersionMatch.Match, serializedCurrent.VersionMatch);

        using var v2Document = JsonDocument.Parse(v2.GetValue());
        var v2CurrentJson = v2Document.RootElement.GetProperty("snapshots")
            .EnumerateArray()
            .Single(snapshot => snapshot.GetProperty("observedProjectorVersion").GetString() == "v2");
        Assert.Equal(now, v2CurrentJson.GetProperty("recordedAtUtc").GetDateTimeOffset());
        Assert.Equal((int)ProjectionStatusVersionMatch.Match, v2CurrentJson.GetProperty("versionMatch").GetInt32());
        Assert.False(v2CurrentJson.GetProperty("snapshot").TryGetProperty("recordedAtUtc", out _));
        Assert.False(v2CurrentJson.GetProperty("snapshot").TryGetProperty("versionMatch", out _));
        Assert.Contains(envelope.GetValue().Snapshots, snapshot =>
            snapshot.ObservedProjectorVersion == "v0" &&
            snapshot.VersionDisposition == ProjectionStatusVersionDisposition.StaleOrOrphan &&
            snapshot.IsStaleOrOrphan);
    }

    public static IEnumerable<object?[]> VersionMatchFreshnessMatrix()
    {
        var now = DateTimeOffset.UtcNow;
        var freshLease = now.AddMinutes(5);
        var oldRecordedAt = now.AddMinutes(-5);
        var expiredLease = now.AddMinutes(-5);

        yield return [
            "fresh-unknown", null, now, freshLease, true,
            ProjectionStatusVersionMatch.Unknown, ProjectionStatusVersionDisposition.Current
        ];
        yield return [
            "fresh-equal", "v1", now, freshLease, true,
            ProjectionStatusVersionMatch.Match, ProjectionStatusVersionDisposition.Current
        ];
        yield return [
            "fresh-unequal", "v2", now, freshLease, true,
            ProjectionStatusVersionMatch.Mismatch, ProjectionStatusVersionDisposition.VersionMismatch
        ];

        // Stale because its committed row timestamp is outside the freshness window, even though its lease is live.
        yield return [
            "stale-recorded-at-unknown", null, oldRecordedAt, freshLease, false,
            ProjectionStatusVersionMatch.Unknown, ProjectionStatusVersionDisposition.StaleOrOrphan
        ];
        // This stale+equal cell kills a resolver that returns StaleOrOrphan before comparing versions.
        yield return [
            "stale-recorded-at-equal", "v1", oldRecordedAt, freshLease, false,
            ProjectionStatusVersionMatch.Match, ProjectionStatusVersionDisposition.StaleOrOrphan
        ];
        // This stale+unequal cell is stale through the independent lease predicate, not through RecordedAtUtc.
        yield return [
            "stale-expired-lease-unequal", "v2", now, expiredLease, false,
            ProjectionStatusVersionMatch.Mismatch, ProjectionStatusVersionDisposition.StaleOrOrphan
        ];
    }

    [Theory]
    [MemberData(nameof(VersionMatchFreshnessMatrix))]
    public async Task Reader_ResolvesVersionMatchIndependentlyAcrossTheSixCellFreshnessMatrix(
        string cellName,
        string? expectedProjectorVersion,
        DateTimeOffset recordedAtUtc,
        DateTimeOffset? leaseExpiresAtUtc,
        bool isFresh,
        ProjectionStatusVersionMatch versionMatch,
        ProjectionStatusVersionDisposition versionDisposition)
    {
        await AssertVersionMatchCellAsync(
            expectedProjectorVersion,
            recordedAtUtc,
            leaseExpiresAtUtc,
            isFresh,
            versionMatch,
            versionDisposition,
            cellName);
    }

    [Fact]
    public async Task Reader_UsesUnknownForBlankExpectedVersionsAndOrdinalVersionMatching()
    {
        var now = DateTimeOffset.UtcNow;
        var freshLease = now.AddMinutes(5);

        await AssertVersionMatchCellAsync(
            expectedProjectorVersion: string.Empty,
            recordedAtUtc: now,
            leaseExpiresAtUtc: freshLease,
            isFresh: true,
            versionMatch: ProjectionStatusVersionMatch.Unknown,
            versionDisposition: ProjectionStatusVersionDisposition.Current);
        await AssertVersionMatchCellAsync(
            expectedProjectorVersion: " \t ",
            recordedAtUtc: now,
            leaseExpiresAtUtc: freshLease,
            isFresh: true,
            versionMatch: ProjectionStatusVersionMatch.Unknown,
            versionDisposition: ProjectionStatusVersionDisposition.Current);
        await AssertVersionMatchCellAsync(
            expectedProjectorVersion: "v1",
            recordedAtUtc: now,
            leaseExpiresAtUtc: freshLease,
            isFresh: true,
            versionMatch: ProjectionStatusVersionMatch.Match,
            versionDisposition: ProjectionStatusVersionDisposition.Current);
        await AssertVersionMatchCellAsync(
            expectedProjectorVersion: "V1",
            recordedAtUtc: now,
            leaseExpiresAtUtc: freshLease,
            isFresh: true,
            versionMatch: ProjectionStatusVersionMatch.Mismatch,
            versionDisposition: ProjectionStatusVersionDisposition.VersionMismatch);
        await AssertVersionMatchCellAsync(
            expectedProjectorVersion: "v2",
            recordedAtUtc: now,
            leaseExpiresAtUtc: freshLease,
            isFresh: true,
            versionMatch: ProjectionStatusVersionMatch.Mismatch,
            versionDisposition: ProjectionStatusVersionDisposition.VersionMismatch);
    }

    [Fact]
    public async Task SerializedV2_UsesAdditiveVersionMatchAndReadsPreG36PayloadDefaults()
    {
        var recordedAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:30+00:00");
        var snapshot = new ProjectionStatusSnapshot(
            "students",
            "v1",
            "cluster-a",
            "activation-a",
            7,
            3,
            "0001",
            "0002",
            4,
            0,
            DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"),
            ProjectionStatusSnapshot.BestEffortConsistency,
            true)
        {
            IsFresh = true,
            RecordedAtUtc = recordedAtUtc,
            ExpectedProjectorVersion = "v1",
            VersionMatch = ProjectionStatusVersionMatch.Match
        };
        var adapter = new SerializedProjectionStatusReader(
            new FixedStatusReader(snapshot),
            new MutableServiceIdProvider("server-service"));

        var serialized = await adapter.ReadSerializedV2Async();
        Assert.True(serialized.IsSuccess);
        var json = Encoding.UTF8.GetString(serialized.GetValue());
        Assert.Contains("\"recordedAtUtc\":\"2026-01-01T00:00:30+00:00\"", json);
        Assert.Contains("\"versionMatch\":1", json);

        var preG36Json = JsonNode.Parse(serialized.GetValue())!.AsObject();
        var preG36Wrapper = preG36Json["snapshots"]!.AsArray()[0]!.AsObject();
        preG36Wrapper.Remove("recordedAtUtc");
        preG36Wrapper.Remove("versionMatch");
        var preG36 = SerializedProjectionStatusReader.DeserializeV2(
            Encoding.UTF8.GetBytes(preG36Json.ToJsonString()));

        Assert.True(preG36.IsSuccess);
        var restored = Assert.Single(preG36.GetValue().Snapshots);
        Assert.Null(restored.RecordedAtUtc);
        Assert.Equal(ProjectionStatusVersionMatch.Unknown, restored.VersionMatch);
        Assert.Null(restored.Snapshot.RecordedAtUtc);
        Assert.Equal(ProjectionStatusVersionMatch.Unknown, restored.Snapshot.VersionMatch);

        var undefinedVersionMatch = SerializedProjectionStatusReader.DeserializeV2(
            Encoding.UTF8.GetBytes(json.Replace("\"versionMatch\":1", "\"versionMatch\":99", StringComparison.Ordinal)));
        Assert.False(undefinedVersionMatch.IsSuccess);
        Assert.IsType<SerializedProjectionStatusShapeException>(undefinedVersionMatch.GetException());
    }

    [Fact]
    public void ProjectionStatusPublicApi_RetainsLegacyClrConstructors()
    {
        Assert.Equal(0, (int)ProjectionStatusVersionDisposition.Current);
        Assert.Equal(1, (int)ProjectionStatusVersionDisposition.VersionMismatch);
        Assert.Equal(2, (int)ProjectionStatusVersionDisposition.StaleOrOrphan);
        Assert.NotNull(typeof(ProjectionStatusWriteResult).GetConstructor(
            [typeof(ProjectionStatusWriteOutcome), typeof(ProjectionStatusHeartbeat), typeof(string)]));
        Assert.NotNull(typeof(ProjectionStatusReadRequest).GetConstructor(
            [typeof(string), typeof(string), typeof(string)]));
        Assert.NotNull(typeof(SerializedProjectionStatusEnvelopeV1).GetConstructor(
            [typeof(int), typeof(string), typeof(IReadOnlyList<ProjectionStatusSnapshot>)]));
        AssertRecordConstructorAndDeconstruct(
            typeof(ProjectionStatusSnapshot),
            [
                typeof(string), typeof(string), typeof(string), typeof(string), typeof(long), typeof(long),
                typeof(string), typeof(string), typeof(long), typeof(long), typeof(DateTimeOffset), typeof(string),
                typeof(bool), typeof(bool), typeof(IReadOnlyList<string>)
            ]);
        AssertRecordConstructorAndDeconstruct(
            typeof(SerializedProjectionStatusSnapshotV2),
            [
                typeof(ProjectionStatusSnapshot), typeof(string), typeof(string),
                typeof(ProjectionStatusVersionDisposition), typeof(bool)
            ]);
    }

    [Fact]
    public async Task Reader_ReusesOneDenominatorPerServiceWindow_AndAggregatesDistinctCursors()
    {
        var provider = new MutableServiceIdProvider("alpha");
        var eventStore = new CountingEventStore(provider) { CursorDelay = TimeSpan.FromMilliseconds(20) };
        var stateStore = new CoreInMemoryMultiProjectionStateStore(provider);
        var now = DateTimeOffset.UtcNow;
        await stateStore.UpsertAsync(CreateHeartbeat("a", 1, now) with
        {
            LastTraversedSortableUniqueId = "0002",
            Phase = ProjectionStatusPhases.Active,
            LeaseExpiresAtUtc = now.AddMinutes(1)
        }, 0);
        await stateStore.UpsertAsync(CreateHeartbeat("b", 1, now) with
        {
            ClusterId = "cluster-b",
            LastTraversedSortableUniqueId = "0002",
            Phase = ProjectionStatusPhases.Active,
            LeaseExpiresAtUtc = now.AddMinutes(1)
        }, 0);
        await stateStore.UpsertAsync(CreateHeartbeat("c", 1, now) with
        {
            ClusterId = "cluster-c",
            LastTraversedSortableUniqueId = "0003",
            Phase = ProjectionStatusPhases.Active,
            LeaseExpiresAtUtc = now.AddMinutes(1)
        }, 0);

        var reader = new ProjectionStatusReader(
            new CountingStatusStore(stateStore),
            eventStore,
            provider,
            new ProjectionStatusOptions { SamplingWindow = TimeSpan.FromMinutes(1), MaxConcurrentReads = 2 });

        var first = await reader.ReadAsync();
        Assert.True(first.IsSuccess);
        Assert.Equal(2, eventStore.CursorCountCalls);
        var second = await reader.ReadAsync();

        Assert.True(second.IsSuccess);
        Assert.Equal(1, eventStore.TotalCountCalls);
        Assert.Equal(4, eventStore.CursorCountCalls); // 0002 is sampled once and 0003 once per read.
        Assert.Equal(2, eventStore.MaxConcurrentCursorReads);
        Assert.All(first.GetValue(), snapshot => Assert.True(snapshot.HasConflict));
        Assert.All(first.GetValue(), snapshot => Assert.False(snapshot.IsCaughtUp));
    }

    [Fact]
    public async Task Reader_CaughtUpRequiresFreshNonFaultedNonConflictRow_AndSupportsEmptyStore()
    {
        var provider = new MutableServiceIdProvider("alpha");
        var eventStore = new CountingEventStore(provider);
        var stateStore = new CoreInMemoryMultiProjectionStateStore(provider);
        var now = DateTimeOffset.UtcNow;

        await stateStore.UpsertAsync(CreateHeartbeat("fresh", 1, now) with
        {
            Phase = ProjectionStatusPhases.Active,
            LeaseExpiresAtUtc = now.AddMinutes(1)
        }, 0);
        var reader = new ProjectionStatusReader(
            stateStore,
            eventStore,
            provider,
            new ProjectionStatusOptions { SamplingWindow = TimeSpan.Zero });
        var empty = await reader.ReadAsync();
        Assert.True(empty.IsSuccess);
        Assert.True(Assert.Single(empty.GetValue()).IsCaughtUp);
        Assert.Equal(0, Assert.Single(empty.GetValue()).TotalEventCount);
        Assert.Equal(0, Assert.Single(empty.GetValue()).RemainingEventCount);

        await stateStore.UpsertAsync(CreateHeartbeat("faulted", 2, now) with
        {
            ClusterId = "cluster-b",
            Phase = ProjectionStatusPhases.Faulted,
            IsFaulted = true,
            FaultMessage = "fold failed",
            LeaseExpiresAtUtc = now.AddMinutes(1)
        }, 0);
        var withFault = await reader.ReadAsync();
        Assert.True(withFault.IsSuccess);
        Assert.Contains(withFault.GetValue(), item => item.IsFaulted && !item.IsCaughtUp);
    }

    [Fact]
    public async Task SqliteStatusStore_UsesReachableUpdateRouteAndFailsClosedForAbsentExpectedSequence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sekiban-projection-status-{Guid.NewGuid():N}.db");
        try
        {
            var provider = new MutableServiceIdProvider("alpha");
            var store = new SqliteMultiProjectionStateStore(path, serviceIdProvider: provider);
            await AssertProjectionStatusCasContractAsync(store);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task SqliteStatusStore_StateBearingDoItTwicePersistsExactRecordedTimestampsAndSequences()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sekiban-projection-status-timestamps-{Guid.NewGuid():N}.db");
        try
        {
            var provider = new MutableServiceIdProvider("alpha");
            var store = new SqliteMultiProjectionStateStore(path, serviceIdProvider: provider);
            var eventStore = new CoreInMemoryEventStore(DomainType.GetDomainTypes().EventTypes, provider);
            var reader = new ProjectionStatusReader(
                store,
                eventStore,
                provider,
                new ProjectionStatusOptions
                {
                    FreshnessWindow = TimeSpan.FromMinutes(30),
                    SamplingWindow = TimeSpan.Zero
                });
            var t1 = DateTimeOffset.UtcNow.AddMinutes(-10);
            var t2 = t1.AddMinutes(1);
            var firstHeartbeat = CreateHeartbeat("writer-a", 1, t1) with
            {
                Phase = ProjectionStatusPhases.Active,
                LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5)
            };

            var firstWrite = await store.UpsertAsync(firstHeartbeat, 0);
            Assert.True(firstWrite.IsSuccess, firstWrite.IsSuccess ? string.Empty : firstWrite.GetException().ToString());
            Assert.True(firstWrite.GetValue().Committed);
            var persistedFirst = Assert.Single((await store.ListAsync("students", "v1")).GetValue());
            Assert.Equal(1, persistedFirst.Sequence);
            Assert.Equal(t1, persistedFirst.RecordedAtUtc);
            var firstSnapshot = Assert.Single((await reader.ReadAsync()).GetValue());
            Assert.Equal(1, firstSnapshot.Sequence);
            Assert.Equal(t1, firstSnapshot.RecordedAtUtc);
            Assert.NotEqual(t1, firstSnapshot.SampledAtUtc);

            var secondHeartbeat = firstHeartbeat with
            {
                Sequence = 2,
                AppliedEventCount = 2,
                RecordedAtUtc = t2
            };
            var secondWrite = await store.UpsertAsync(secondHeartbeat, 1);
            Assert.True(secondWrite.IsSuccess, secondWrite.IsSuccess ? string.Empty : secondWrite.GetException().ToString());
            Assert.True(secondWrite.GetValue().Committed);
            var persistedSecond = Assert.Single((await store.ListAsync("students", "v1")).GetValue());
            Assert.Equal(2, persistedSecond.Sequence);
            Assert.Equal(t2, persistedSecond.RecordedAtUtc);
            var secondSnapshot = Assert.Single((await reader.ReadAsync()).GetValue());
            Assert.Equal(2, secondSnapshot.Sequence);
            Assert.Equal(t2, secondSnapshot.RecordedAtUtc);
            Assert.NotEqual(t2, secondSnapshot.SampledAtUtc);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task InMemoryStatusStore_UsesTheSharedFailClosedCasContract()
    {
        await AssertProjectionStatusCasContractAsync(
            new CoreInMemoryMultiProjectionStateStore(new MutableServiceIdProvider("alpha")));
    }

    [Fact]
    public async Task CosmosStatusStore_UsesTheSharedFailClosedCasContract()
    {
        var client = new InMemoryCosmosClient();
        var options = new CosmosDbEventStoreOptions
        {
            MultiProjectionStatesContainerName = "multiProjectionStates"
        };
        var store = new CosmosMultiProjectionStateStore(
            new CosmosDbContext(client, "test-db", options: options),
            new MutableServiceIdProvider("alpha"),
            new DefaultCosmosContainerResolver(options));

        await AssertProjectionStatusCasContractAsync(store);
    }

    [Fact]
    public async Task DynamoStatusStore_UsesTheSharedFailClosedCasContract()
    {
        var client = DispatchProxy.Create<IAmazonDynamoDB, MixedDynamoDb>();
        var store = new DynamoMultiProjectionStateStore(
            new DynamoDbContext(
                client,
                Options.Create(new DynamoDbEventStoreOptions
                {
                    AutoCreateTables = false,
                    ProjectionStatesTableName = "states"
                })),
            new MutableServiceIdProvider("alpha"));

        await AssertProjectionStatusCasContractAsync(store);
    }

    [Fact]
    public void ProviderStatusRows_UseClusterIdentity_AndPreserveMixedDocumentCompatibility()
    {
        var now = DateTimeOffset.UtcNow;
        var first = CreateHeartbeat("activation-a", 1, now) with { Phase = ProjectionStatusPhases.Active };
        var replacement = first with { ActivationId = "activation-b", Sequence = 2 };

        var cosmosKey = typeof(Sekiban.Dcb.CosmosDb.CosmosMultiProjectionStateStore)
            .GetMethod("BuildStatusId", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { first });
        var replacementCosmosKey = typeof(Sekiban.Dcb.CosmosDb.CosmosMultiProjectionStateStore)
            .GetMethod("BuildStatusId", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { replacement });
        Assert.Equal(cosmosKey, replacementCosmosKey);

        var dynamoKey = typeof(Sekiban.Dcb.DynamoDB.DynamoMultiProjectionStateStore)
            .GetMethod("BuildStatusSortKey", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { first });
        var replacementDynamoKey = typeof(Sekiban.Dcb.DynamoDB.DynamoMultiProjectionStateStore)
            .GetMethod("BuildStatusSortKey", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { replacement });
        Assert.Equal(dynamoKey, replacementDynamoKey);

        var cosmos = Sekiban.Dcb.CosmosDb.Models.CosmosMultiProjectionState.FromStatusHeartbeat(
            first,
            (string)cosmosKey!,
            "MultiProjectionState_students",
            "alpha|MultiProjectionState_students");
        Assert.Equal(first.ActivationId, cosmos.ToStatusHeartbeat().ActivationId);
        Assert.Equal(first.Phase, cosmos.ToStatusHeartbeat().Phase);

        var dynamo = Sekiban.Dcb.DynamoDB.Models.DynamoMultiProjectionState.FromStatusHeartbeat(
            first,
            "alpha",
            (string)dynamoKey!);
        var attributes = dynamo.ToAttributeValues();
        Assert.Equal("projectionStatus", attributes["documentType"].S);
        Assert.Equal(first.ActivationId, Sekiban.Dcb.DynamoDB.Models.DynamoMultiProjectionState
            .FromAttributeValues(attributes).ToStatusHeartbeat().ActivationId);

        // A legacy snapshot without documentType remains a projection-state document, not a status row.
        var legacy = new Dictionary<string, Amazon.DynamoDBv2.Model.AttributeValue>
        {
            ["pk"] = new() { S = "SERVICE#alpha#PROJECTOR#students" },
            ["sk"] = new() { S = "VERSION#v1" },
            ["serviceId"] = new() { S = "alpha" },
            ["projectorName"] = new() { S = "students" },
            ["projectorVersion"] = new() { S = "v1" },
            ["lastSortableUniqueId"] = new() { S = "0001" }
        };
        Assert.Equal("students", Sekiban.Dcb.DynamoDB.Models.DynamoMultiProjectionState
            .FromAttributeValues(legacy).ToRecord().ProjectorName);
    }

    [Fact]
    public async Task CosmosMixedDocuments_KeepStatusOutOfSnapshotListRestoreDeleteAll_AndCheckpointCas()
    {
        var client = new InMemoryCosmosClient();
        var store = new CosmosMultiProjectionStateStore(
            new CosmosDbContext(
                client,
                "test-db",
                options: new CosmosDbEventStoreOptions
                {
                    MultiProjectionStatesContainerName = "multiProjectionStates"
                }),
            new MutableServiceIdProvider("alpha"),
            new DefaultCosmosContainerResolver(new CosmosDbEventStoreOptions
            {
                MultiProjectionStatesContainerName = "multiProjectionStates"
            }));
        var status = CreateHeartbeat("status-activation", 1, DateTimeOffset.UtcNow);

        Assert.True((await store.UpsertAsync(status, 0)).GetValue().Committed);

        var checkpoint = (IGenerationAwareCheckpointStore)store;
        var created = await checkpoint.ConditionalUpsertAsync(
            CreateProjectionWriteRequest(),
            new MemoryStream(Encoding.UTF8.GetBytes("snapshot")),
            CheckpointExpectation.Absent,
            1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, created.Status);

        var listed = await store.ListAllAsync();
        Assert.True(listed.IsSuccess);
        Assert.Single(listed.GetValue());
        Assert.Equal("students", listed.GetValue()[0].ProjectorName);
        Assert.Single((await store.ListAsync()).GetValue());

        var restored = await store.GetLatestForVersionAsync("students", "v1");
        Assert.True(restored.IsSuccess);
        Assert.True(restored.GetValue().HasValue);

        var deleted = await store.DeleteAllAsync();
        Assert.True(deleted.IsSuccess);
        Assert.Equal(1, deleted.GetValue());
        Assert.Single((await store.ListAsync()).GetValue());

        // A discriminator-less pre-G24 snapshot is accepted by the restore path after status rows have been written.
        var legacy = CosmosMultiProjectionState.FromRecord(
            CreateProjectionWriteRequest().ToRecord(),
            "alpha");
        legacy.DocumentType = null;
        client.Container("multiProjectionStates").Seed(legacy);
        var legacyRestored = await store.GetLatestForVersionAsync("students", "v1");
        Assert.True(legacyRestored.IsSuccess);
        Assert.True(legacyRestored.GetValue().HasValue);
        Assert.Equal("students", legacyRestored.GetValue().Value!.ProjectorName);

        var deletedLegacy = await store.DeleteAllAsync("students");
        Assert.True(deletedLegacy.IsSuccess);
        Assert.Equal(1, deletedLegacy.GetValue());
        Assert.Empty((await store.ListAllAsync()).GetValue());
        Assert.Single((await store.ListAsync()).GetValue());
    }

    [Fact]
    public async Task DynamoMixedDocuments_KeepStatusOutOfSnapshotListRestoreDeleteAll_AndCheckpointCas()
    {
        var client = DispatchProxy.Create<IAmazonDynamoDB, MixedDynamoDb>();
        var store = new DynamoMultiProjectionStateStore(
            new DynamoDbContext(
                client,
                Options.Create(new DynamoDbEventStoreOptions
                {
                    AutoCreateTables = false,
                    ProjectionStatesTableName = "states"
                })),
            new MutableServiceIdProvider("alpha"));
        var status = CreateHeartbeat("status-activation", 1, DateTimeOffset.UtcNow);

        Assert.True((await store.UpsertAsync(status, 0)).GetValue().Committed);

        var checkpoint = (IGenerationAwareCheckpointStore)store;
        var created = await checkpoint.ConditionalUpsertAsync(
            CreateProjectionWriteRequest(),
            new MemoryStream(Encoding.UTF8.GetBytes("snapshot")),
            CheckpointExpectation.Absent,
            1_000_000);
        Assert.Equal(CheckpointCasStatus.Committed, created.Status);

        var listed = await store.ListAllAsync();
        Assert.True(listed.IsSuccess);
        Assert.Single(listed.GetValue());
        Assert.Single((await store.ListAsync()).GetValue());

        var restored = await store.GetLatestForVersionAsync("students", "v1");
        Assert.True(restored.IsSuccess);
        Assert.True(restored.GetValue().HasValue);

        var deleted = await store.DeleteAllAsync();
        Assert.True(deleted.IsSuccess);
        Assert.Equal(1, deleted.GetValue());
        Assert.Single((await store.ListAsync()).GetValue());

        // A discriminator-less pre-G24 snapshot is accepted by Dynamo restore and snapshot cleanup paths.
        var legacy = Sekiban.Dcb.DynamoDB.Models.DynamoMultiProjectionState
            .FromRecord(CreateProjectionWriteRequest().ToRecord(), "alpha")
            .ToAttributeValues();
        legacy.Remove("documentType");
        MixedDynamoDb.LastCreated!.Seed(legacy);

        var legacyRestored = await store.GetLatestForVersionAsync("students", "v1");
        Assert.True(legacyRestored.IsSuccess);
        Assert.True(legacyRestored.GetValue().HasValue);
        Assert.Equal("students", legacyRestored.GetValue().Value!.ProjectorName);

        var deletedLegacy = await store.DeleteAllAsync("students");
        Assert.True(deletedLegacy.IsSuccess);
        Assert.Equal(1, deletedLegacy.GetValue());
        Assert.Empty((await store.ListAllAsync()).GetValue());
        Assert.Single((await store.ListAsync()).GetValue());
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

    private static async Task AssertVersionMatchCellAsync(
        string? expectedProjectorVersion,
        DateTimeOffset recordedAtUtc,
        DateTimeOffset? leaseExpiresAtUtc,
        bool isFresh,
        ProjectionStatusVersionMatch versionMatch,
        ProjectionStatusVersionDisposition versionDisposition,
        string? cellName = null)
    {
        var provider = new MutableServiceIdProvider("alpha");
        var statusStore = new CoreInMemoryMultiProjectionStateStore(provider);
        var eventStore = new CoreInMemoryEventStore(DomainType.GetDomainTypes().EventTypes, provider);
        var heartbeat = CreateHeartbeat("activation-a", 1, recordedAtUtc) with
        {
            Phase = ProjectionStatusPhases.Active,
            LeaseExpiresAtUtc = leaseExpiresAtUtc
        };
        var write = await statusStore.UpsertAsync(heartbeat, 0);
        Assert.True(write.IsSuccess, write.IsSuccess ? string.Empty : write.GetException().ToString());
        Assert.True(write.GetValue().Committed);

        var reader = new ProjectionStatusReader(
            statusStore,
            eventStore,
            provider,
            new ProjectionStatusOptions
            {
                FreshnessWindow = TimeSpan.FromMinutes(2),
                SamplingWindow = TimeSpan.Zero
            });
        var request = new ProjectionStatusReadRequest(ProjectorName: "students")
        {
            ExpectedProjectorVersion = expectedProjectorVersion
        };
        var result = await reader.ReadAsync(request);

        Assert.True(
            result.IsSuccess,
            result.IsSuccess
                ? string.Empty
                : $"{cellName ?? "version-match cell"}: {result.GetException()}");
        var snapshot = Assert.Single(result.GetValue());
        Assert.Equal(recordedAtUtc, snapshot.RecordedAtUtc);
        Assert.Equal(isFresh, snapshot.IsFresh);
        Assert.Equal(versionMatch, snapshot.VersionMatch);
        Assert.Equal(versionDisposition, snapshot.VersionDisposition);
        Assert.Equal(
            versionDisposition == ProjectionStatusVersionDisposition.StaleOrOrphan,
            snapshot.IsStaleOrOrphan);
    }

    private static void AssertRecordConstructorAndDeconstruct(Type recordType, Type[] parameterTypes)
    {
        var constructor = recordType.GetConstructor(parameterTypes);
        Assert.NotNull(constructor);
        Assert.Equal(parameterTypes.Length, constructor.GetParameters().Length);

        var deconstruct = recordType.GetMethod(
            "Deconstruct",
            parameterTypes.Select(parameterType => parameterType.MakeByRefType()).ToArray());
        Assert.NotNull(deconstruct);
    }

    /// <summary>
    ///     Shared provider matrix for the public status-store contract. Each invocation drives the production store,
    ///     including its native conditional create/update operation; it is deliberately not a mocked write result.
    /// </summary>
    private static async Task AssertProjectionStatusCasContractAsync(IProjectionStatusStore store)
    {
        var first = CreateHeartbeat("activation-a", 1, DateTimeOffset.UtcNow);
        var created = await store.UpsertAsync(first, 0);
        Assert.True(created.IsSuccess, created.IsSuccess ? string.Empty : created.GetException().ToString());
        Assert.True(created.GetValue().Committed);

        var updatedHeartbeat = first with { Sequence = 2, AppliedEventCount = 2 };
        var updated = await store.UpsertAsync(updatedHeartbeat, 1);
        Assert.True(updated.IsSuccess, updated.IsSuccess ? string.Empty : updated.GetException().ToString());
        Assert.True(updated.GetValue().Committed);
        Assert.Equal(2, updated.GetValue().Current!.Sequence);
        Assert.Equal(2, updated.GetValue().Current!.AppliedEventCount);
        var rowsAfterUpdate = await store.ListAsync("students", "v1");
        Assert.True(rowsAfterUpdate.IsSuccess, rowsAfterUpdate.IsSuccess ? string.Empty : rowsAfterUpdate.GetException().ToString());
        var rowAfterUpdate = Assert.Single(rowsAfterUpdate.GetValue());
        Assert.Equal(2, rowAfterUpdate.Sequence);
        Assert.Equal(2, rowAfterUpdate.AppliedEventCount);

        var createRace = await store.UpsertAsync(first with { Sequence = 3 }, 0);
        Assert.True(createRace.IsSuccess, createRace.IsSuccess ? string.Empty : createRace.GetException().ToString());
        var createRaceConflict = Assert.IsType<ProjectionStatusWriteConflict>(createRace.GetValue().ConflictDetails);
        Assert.Equal(ProjectionStatusConflictReason.RowAlreadyExists, createRaceConflict.Reason);
        Assert.Equal(0, createRaceConflict.ExpectedSequence);
        Assert.Equal(2, createRaceConflict.ObservedSequence);
        Assert.Equal(first.ProjectorVersion, createRaceConflict.ExpectedProjectorVersion);
        Assert.Equal(first.ProjectorVersion, createRaceConflict.ObservedProjectorVersion);
        Assert.Equal(createRaceConflict.ToCompatibilityReason(), createRace.GetValue().ConflictReason);
        Assert.DoesNotContain("activation row already exists", createRace.GetValue().ConflictReason!, StringComparison.OrdinalIgnoreCase);

        var staleUpdate = await store.UpsertAsync(first with { Sequence = 3 }, 1);
        Assert.True(staleUpdate.IsSuccess, staleUpdate.IsSuccess ? string.Empty : staleUpdate.GetException().ToString());
        Assert.True(staleUpdate.GetValue().Conflict, staleUpdate.GetValue().ToString());
        var staleUpdateConflict = Assert.IsType<ProjectionStatusWriteConflict>(staleUpdate.GetValue().ConflictDetails);
        Assert.Equal(ProjectionStatusConflictReason.SequenceMismatch, staleUpdateConflict.Reason);
        Assert.Equal(1, staleUpdateConflict.ExpectedSequence);
        Assert.Equal(2, staleUpdateConflict.ObservedSequence);

        var missing = first with { ClusterId = "missing-cluster", Sequence = 2 };
        var absent = await store.UpsertAsync(missing, 1);
        Assert.True(absent.IsSuccess, absent.IsSuccess ? string.Empty : absent.GetException().ToString());
        var absentConflict = Assert.IsType<ProjectionStatusWriteConflict>(absent.GetValue().ConflictDetails);
        Assert.Equal(ProjectionStatusConflictReason.RowAbsent, absentConflict.Reason);
        Assert.Equal(1, absentConflict.ExpectedSequence);
        Assert.Null(absentConflict.ObservedSequence);
        Assert.Equal(missing.ProjectorVersion, absentConflict.ExpectedProjectorVersion);
        Assert.Null(absentConflict.ObservedProjectorVersion);

        var afterAbsent = await store.ListAsync("students", "v1");
        Assert.True(afterAbsent.IsSuccess, afterAbsent.IsSuccess ? string.Empty : afterAbsent.GetException().ToString());
        Assert.DoesNotContain(afterAbsent.GetValue(), row => row.ClusterId == "missing-cluster");

        // A later, still-conditional create can lose a race. It must report the winner rather than overwrite it.
        var competingCreate = missing with { ActivationId = "competing", Sequence = 1 };
        var competitor = await store.UpsertAsync(competingCreate, 0);
        Assert.True(competitor.IsSuccess, competitor.IsSuccess ? string.Empty : competitor.GetException().ToString());
        Assert.True(competitor.GetValue().Committed);

        var losingCreate = await store.UpsertAsync(
            missing with { ActivationId = "retrying", Sequence = 1 },
            0);
        Assert.True(losingCreate.IsSuccess, losingCreate.IsSuccess ? string.Empty : losingCreate.GetException().ToString());
        var losingConflict = Assert.IsType<ProjectionStatusWriteConflict>(losingCreate.GetValue().ConflictDetails);
        Assert.Equal(ProjectionStatusConflictReason.RowAlreadyExists, losingConflict.Reason);
        Assert.Equal(1, losingConflict.ObservedSequence);
        Assert.Equal("competing", losingConflict.ObservedActivationId);

        var rebasedUpdate = await store.UpsertAsync(
            missing with { ActivationId = "retrying", Sequence = 2 },
            1);
        Assert.True(rebasedUpdate.IsSuccess, rebasedUpdate.IsSuccess ? string.Empty : rebasedUpdate.GetException().ToString());
        Assert.True(rebasedUpdate.GetValue().Committed);
        Assert.Equal(2, rebasedUpdate.GetValue().Current!.Sequence);
    }

    private static MultiProjectionStateWriteRequest CreateProjectionWriteRequest() => new(
        "students",
        "v1",
        "Payload",
        "0001",
        1,
        false,
        null,
        null,
        1,
        1,
        "0000",
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        "test",
        null);

    private sealed class FixedStatusReader : IProjectionStatusReader
    {
        private readonly IReadOnlyList<ProjectionStatusSnapshot> _snapshots;

        public FixedStatusReader(ProjectionStatusSnapshot snapshot) => _snapshots = new[] { snapshot };

        public Task<ResultBox<IReadOnlyList<ProjectionStatusSnapshot>>> ReadAsync(
            ProjectionStatusReadRequest? request = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResultBox.FromValue(_snapshots));
    }

    private sealed class CountingStatusStore : IProjectionStatusStore
    {
        private readonly IProjectionStatusStore _inner;

        public CountingStatusStore(IProjectionStatusStore inner) => _inner = inner;

        public int ListCalls { get; private set; }

        public Task<ResultBox<ProjectionStatusWriteResult>> UpsertAsync(
            ProjectionStatusHeartbeat heartbeat,
            long expectedSequence,
            CancellationToken cancellationToken = default) =>
            _inner.UpsertAsync(heartbeat, expectedSequence, cancellationToken);

        public Task<ResultBox<IReadOnlyList<ProjectionStatusHeartbeat>>> ListAsync(
            string? projectorName = null,
            string? projectorVersion = null,
            CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return _inner.ListAsync(projectorName, projectorVersion, cancellationToken);
        }
    }

    private sealed class CountingEventStore : IEventStore
    {
        private readonly CoreInMemoryEventStore _inner;
        private int _activeCursorReads;

        public CountingEventStore(IServiceIdProvider provider)
        {
            _inner = new CoreInMemoryEventStore(DomainType.GetDomainTypes().EventTypes, provider);
        }

        public int TotalCountCalls { get; private set; }
        public int CursorCountCalls { get; private set; }
        public int MaxConcurrentCursorReads { get; private set; }
        public TimeSpan CursorDelay { get; set; }

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => _inner.ReadTagsAsync(tag);
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => _inner.GetLatestTagAsync(tag);
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => _inner.TagExistsAsync(tag);

        public async Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null)
        {
            if (since is null)
            {
                TotalCountCalls++;
                return await _inner.GetEventCountAsync(since);
            }

            CursorCountCalls++;
            var active = Interlocked.Increment(ref _activeCursorReads);
            MaxConcurrentCursorReads = Math.Max(MaxConcurrentCursorReads, active);
            try
            {
                if (CursorDelay > TimeSpan.Zero)
                {
                    await Task.Delay(CursorDelay);
                }

                return await _inner.GetEventCountAsync(since);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCursorReads);
            }
        }

        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => _inner.GetAllTagsAsync(tagGroup);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) => _inner.ReadAllSerializableEventsAsync(since);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? maxCount) => _inner.ReadAllSerializableEventsAsync(since, maxCount);
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) => _inner.ReadSerializableEventAsync(eventId);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) => _inner.ReadSerializableEventsByTagAsync(tag, since);
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) => _inner.WriteSerializableEventsAsync(events);
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => _inner.GetLatestSortableUniqueIdAsync();
    }

    private class MixedDynamoDb : DispatchProxy
    {
        private readonly Dictionary<(string Pk, string Sk), Dictionary<string, AttributeValue>> _items = new();
        private readonly object _gate = new();

        public static MixedDynamoDb? LastCreated { get; private set; }

        public MixedDynamoDb() => LastCreated = this;

        public void Seed(Dictionary<string, AttributeValue> item)
        {
            lock (_gate)
            {
                _items[KeyOf(item)] = Clone(item);
            }
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod?.Name ?? string.Empty;
            var request = args is { Length: > 0 } ? args[0] : null;
            return request switch
            {
                GetItemRequest get when name == nameof(IAmazonDynamoDB.GetItemAsync) => Task.FromResult(Get(get)),
                PutItemRequest put when name == nameof(IAmazonDynamoDB.PutItemAsync) => Task.FromResult(Put(put)),
                ScanRequest scan when name == nameof(IAmazonDynamoDB.ScanAsync) => Task.FromResult(Scan(scan)),
                QueryRequest query when name == nameof(IAmazonDynamoDB.QueryAsync) => Task.FromResult(Query(query)),
                DeleteItemRequest delete when name == nameof(IAmazonDynamoDB.DeleteItemAsync) => Task.FromResult(Delete(delete)),
                _ when name == "Dispose" => null,
                _ when name.StartsWith("get_", StringComparison.Ordinal) => null,
                _ => throw new NotSupportedException($"MixedDynamoDb does not support {name}")
            };
        }

        private static (string Pk, string Sk) KeyOf(Dictionary<string, AttributeValue> item) =>
            (item["pk"].S, item["sk"].S);

        private static Dictionary<string, AttributeValue> Clone(Dictionary<string, AttributeValue> item) =>
            item.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        private GetItemResponse Get(GetItemRequest request)
        {
            lock (_gate)
            {
                var key = (request.Key["pk"].S, request.Key["sk"].S);
                return new GetItemResponse
                {
                    Item = _items.TryGetValue(key, out var item)
                        ? Clone(item)
                        : new Dictionary<string, AttributeValue>()
                };
            }
        }

        private PutItemResponse Put(PutItemRequest request)
        {
            lock (_gate)
            {
                var key = KeyOf(request.Item);
                _items.TryGetValue(key, out var current);
                if (!Evaluate(
                        request.ConditionExpression,
                        current,
                        request.ExpressionAttributeNames,
                        request.ExpressionAttributeValues))
                {
                    throw new ConditionalCheckFailedException("The conditional request failed");
                }

                _items[key] = Clone(request.Item);
                return new PutItemResponse();
            }
        }

        private ScanResponse Scan(ScanRequest request)
        {
            lock (_gate)
            {
                var rows = Filter(_items.Values, request.FilterExpression, request.ExpressionAttributeValues).ToList();
                return new ScanResponse
                {
                    Items = rows.Select(Clone).ToList(),
                    LastEvaluatedKey = new Dictionary<string, AttributeValue>()
                };
            }
        }

        private QueryResponse Query(QueryRequest request)
        {
            lock (_gate)
            {
                var pk = request.ExpressionAttributeValues![":pk"].S;
                var rows = Filter(
                    _items.Values.Where(item => item["pk"].S == pk),
                    request.FilterExpression,
                    request.ExpressionAttributeValues).ToList();
                if (request.FilterExpression?.Contains("documentType", StringComparison.Ordinal) == true)
                {
                    rows = rows.OrderByDescending(item =>
                        long.TryParse(item.GetValueOrDefault("eventsProcessed")?.N, out var count) ? count : 0).ToList();
                }

                return new QueryResponse
                {
                    Items = rows.Select(Clone).ToList(),
                    LastEvaluatedKey = new Dictionary<string, AttributeValue>()
                };
            }
        }

        private DeleteItemResponse Delete(DeleteItemRequest request)
        {
            lock (_gate)
            {
                _items.Remove((request.Key["pk"].S, request.Key["sk"].S));
                return new DeleteItemResponse();
            }
        }

        private static IEnumerable<Dictionary<string, AttributeValue>> Filter(
            IEnumerable<Dictionary<string, AttributeValue>> source,
            string? expression,
            Dictionary<string, AttributeValue>? values)
        {
            var rows = source;
            if (values?.TryGetValue(":serviceId", out var serviceId) == true)
            {
                rows = rows.Where(item => item.GetValueOrDefault("serviceId")?.S == serviceId.S);
            }

            if (values?.TryGetValue(":projectorName", out var projectorName) == true)
            {
                rows = rows.Where(item => item.GetValueOrDefault("projectorName")?.S == projectorName.S);
            }

            if (values?.TryGetValue(":projectorVersion", out var projectorVersion) == true)
            {
                rows = rows.Where(item => item.GetValueOrDefault("projectorVersion")?.S == projectorVersion.S);
            }

            if (expression?.Contains("documentType = :statusType", StringComparison.Ordinal) == true)
            {
                rows = rows.Where(item => item.GetValueOrDefault("documentType")?.S == "projectionStatus");
            }
            else if (expression?.Contains("attribute_not_exists(documentType)", StringComparison.Ordinal) == true)
            {
                rows = rows.Where(item =>
                    !item.ContainsKey("documentType") || item.GetValueOrDefault("documentType")?.S == "projectionState");
            }

            return rows;
        }

        private static bool Evaluate(
            string? expression,
            Dictionary<string, AttributeValue>? current,
            Dictionary<string, string>? names,
            Dictionary<string, AttributeValue>? values)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return true;
            }

            if (expression.Contains("attribute_not_exists(pk)", StringComparison.Ordinal))
            {
                return current is null;
            }

            if (current is null || values is null)
            {
                return false;
            }

            foreach (var clause in expression.Split(" AND ", StringSplitOptions.None))
            {
                var comparison = clause.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (comparison.Length != 3)
                {
                    return false;
                }

                var attribute = comparison[0].StartsWith('#') && names is not null
                    ? names[comparison[0]]
                    : comparison[0];
                if (!current.TryGetValue(attribute, out var actual) ||
                    !values.TryGetValue(comparison[2], out var expected))
                {
                    return false;
                }

                var actualNumber = long.TryParse(actual.N, out var actualN) ? actualN : 0;
                var expectedNumber = long.TryParse(expected.N, out var expectedN) ? expectedN : 0;
                var matches = comparison[1] switch
                {
                    "=" => actual.N is not null || expected.N is not null
                        ? string.Equals(actual.N, expected.N, StringComparison.Ordinal)
                        : string.Equals(actual.S, expected.S, StringComparison.Ordinal),
                    "<" => actualNumber < expectedNumber,
                    _ => false
                };
                if (!matches)
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class MutableServiceIdProvider : IServiceIdProvider
    {
        private readonly string _serviceId;

        public MutableServiceIdProvider(string serviceId) => _serviceId = serviceId;

        public string GetCurrentServiceId() => ServiceIdValidator.NormalizeAndValidate(_serviceId);
    }
}
