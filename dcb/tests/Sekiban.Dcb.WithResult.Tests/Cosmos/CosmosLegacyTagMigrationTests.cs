using Dcb.Domain;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.CosmosDb.Migration;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.CosmosDb.Repair;
using Sekiban.Dcb.CosmosDb.Sweep;
using Sekiban.Dcb.CosmosDb.Tags;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using System.Reflection;

namespace Sekiban.Dcb.Tests.Cosmos;

/// <summary>
///     The destructive legacy tag-row migration, driven end to end against the in-memory Cosmos harness.
///     This is the only thing in the provider that deletes a tag row, so the tests are mostly about the locks
///     rather than the lock-picking: that it refuses without a plan, refuses without a confirm, refuses
///     without a backup, refuses a plan it was not given, backs up before it deletes, and never forces a
///     delete past a row that moved under it. The reduction itself is almost the easy part.
/// </summary>
public class CosmosLegacyTagMigrationTests
{
    private const string ServiceId = "svc";
    private const string OtherServiceId = "other";
    private const string Tag = "Student:1";

    private sealed class Lineage
    {
        public Lineage(string serviceId, InMemoryCosmosClient client, string events, string tags)
        {
            ServiceId = serviceId;
            Client = client;

            Options = new CosmosDbEventStoreOptions
            {
                EventsContainerName = events,
                TagsContainerName = tags,
                WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward,
                TagWriteRetry = new CosmosTagWriteRetryOptions { MaxAttempts = 1 }
            };

            Context = new CosmosDbContext(client, "test-db", null, Options);
            Resolver = new DefaultCosmosContainerResolver(Options);
            Store = new CosmosDbEventStore(
                Context,
                DomainType.GetDomainTypes().EventTypes,
                new FixedServiceId(serviceId),
                Resolver);
            MigrationFactory = new CosmosDbLegacyTagMigrationServiceFactory(Context, Resolver);
            RepairFactory = new CosmosDbTagRepairServiceFactory(Context, Resolver);
        }

        public string ServiceId { get; }
        public InMemoryCosmosClient Client { get; }
        public CosmosDbEventStoreOptions Options { get; }
        public CosmosDbContext Context { get; }
        public DefaultCosmosContainerResolver Resolver { get; }
        public CosmosDbEventStore Store { get; }
        public CosmosDbLegacyTagMigrationServiceFactory MigrationFactory { get; }
        public CosmosDbTagRepairServiceFactory RepairFactory { get; }

        public InMemoryCosmosContainer Events => Client.Container(Options.EventsContainerName);
        public InMemoryCosmosContainer Tags => Client.Container(Options.TagsContainerName);

        public Task<CosmosDbLegacyTagMigrationService> MigrationAsync() => MigrationFactory.CreateAsync(ServiceId);
    }

    private sealed class FixedServiceId : IServiceIdProvider
    {
        private readonly string _serviceId;
        public FixedServiceId(string serviceId) => _serviceId = serviceId;
        public string GetCurrentServiceId() => _serviceId;
    }

    /// <summary>Records what it was handed, so "backed up before anything was deleted" can be asserted.</summary>
    private sealed class RecordingBackupWriter : ICosmosTagMigrationBackupWriter
    {
        private readonly Func<int>? _deletesSoFar;

        public RecordingBackupWriter(Func<int>? deletesSoFar = null) => _deletesSoFar = deletesSoFar;

        public List<CosmosTag> Rows { get; } = new();
        public int DeletesWhenCalled { get; private set; } = -1;
        public int Calls { get; private set; }

        public Task WriteAsync(
            CosmosTagMigrationPlan plan,
            IReadOnlyList<CosmosTag> rowsToRemove,
            CancellationToken cancellationToken)
        {
            Calls++;
            DeletesWhenCalled = _deletesSoFar?.Invoke() ?? -1;
            Rows.AddRange(rowsToRemove);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingBackupWriter : ICosmosTagMigrationBackupWriter
    {
        public Task WriteAsync(
            CosmosTagMigrationPlan plan,
            IReadOnlyList<CosmosTag> rowsToRemove,
            CancellationToken cancellationToken) =>
            throw new IOException("the backup could not be written");
    }

    private static Lineage NewLineage(
        string serviceId = ServiceId,
        InMemoryCosmosClient? client = null,
        string events = "events",
        string tags = "tags") =>
        new(serviceId, client ?? new InMemoryCosmosClient(), events, tags);

    private static SerializableEvent NewEvent(string tag) =>
        new(
            System.Text.Encoding.UTF8.GetBytes("""{"Name":"test"}"""),
            SortableUniqueId.GenerateNew(),
            Guid.NewGuid(),
            new EventMetadata("causation", "correlation", "user"),
            new List<string> { tag },
            "TestEventPayload");

    /// <summary>A row as the pre-SEK-G2 writer produced it: a random document id.</summary>
    private static CosmosTag LegacyRow(string serviceId, string tag, SerializableEvent source) =>
        new()
        {
            Pk = $"{serviceId}|{tag}",
            ServiceId = serviceId,
            Id = Guid.NewGuid().ToString(),
            Tag = tag,
            TagGroup = tag.Split(':')[0],
            EventType = source.EventPayloadName,
            SortableUniqueId = source.SortableUniqueIdValue,
            EventId = source.Id.ToString(),
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    /// <summary>Writes an event whose tag rows never landed, then seeds legacy rows for it.</summary>
    private static async Task<SerializableEvent> SeedLegacyOnlyEventAsync(Lineage lineage, int legacyRows)
    {
        var written = NewEvent(Tag);
        lineage.Store.TagWriteFaultInjector = new NeverWriteTags();
        await lineage.Store.WriteSerializableEventsAsync(new[] { written });
        lineage.Store.TagWriteFaultInjector = null;

        for (var i = 0; i < legacyRows; i++)
        {
            lineage.Tags.Seed(LegacyRow(lineage.ServiceId, Tag, written));
        }

        return written;
    }

    private sealed class NeverWriteTags : ICosmosTagWriteFaultInjector
    {
        public Task OnBeforeBatchAsync(int batchIndex, string partitionKey, IReadOnlyList<CosmosTag> rows) =>
            throw new InvalidOperationException("Injected: the tag write never happened");
    }

    private static CosmosTagMigrationApplyOptions Authorized(ICosmosTagMigrationBackupWriter backup) =>
        new() { Confirm = true, BackupWriter = backup };

    // ── The plan mutates nothing ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Planning_Should_Describe_The_Reduction_And_Change_Nothing()
    {
        var lineage = NewLineage();
        var written = await SeedLegacyOnlyEventAsync(lineage, legacyRows: 2);

        var migration = await lineage.MigrationAsync();
        var plan = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());

        var action = Assert.Single(plan.Actions);
        Assert.Equal(written.Id, action.EventId);
        Assert.Equal(written.Id.ToString(), action.SurvivorId); // the canonical row, always
        Assert.False(action.SurvivorExists);                    // only legacy rows are here; it will be created
        Assert.Equal(2, action.RowsToRemove.Count);
        Assert.All(action.RowsToRemove, row => Assert.NotNull(row.ETag));

        // A dry run is a dry run.
        Assert.Equal(2, lineage.Tags.Items.Count);
        Assert.Equal(0, lineage.Tags.Deletes);
    }

    [Fact]
    public async Task Planning_The_Same_Unchanged_World_Twice_Should_Produce_The_Same_Artifact()
    {
        var lineage = NewLineage();
        await SeedLegacyOnlyEventAsync(lineage, legacyRows: 3);

        var migration = await lineage.MigrationAsync();
        var first = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());
        var second = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());

        // The plan an operator reads and the run that executes it are two passes over the data. If they
        // could disagree, the artifact would not describe the run.
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.ToJson(), second.ToJson());
    }

    // ── The authorization gate ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Applying_Without_A_Confirm_Should_Refuse_And_Delete_Nothing()
    {
        var lineage = NewLineage();
        await SeedLegacyOnlyEventAsync(lineage, legacyRows: 2);

        var migration = await lineage.MigrationAsync();
        var plan = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());

        await Assert.ThrowsAsync<CosmosTagMigrationNotAuthorizedException>(
            () => migration.ApplyAsync(
                plan,
                new CosmosTagMigrationApplyOptions { BackupWriter = new RecordingBackupWriter() }));

        Assert.Equal(2, lineage.Tags.Items.Count);
        Assert.Equal(0, lineage.Tags.Deletes);
    }

    [Fact]
    public async Task Applying_Without_A_Backup_Writer_Should_Refuse_And_Delete_Nothing()
    {
        var lineage = NewLineage();
        await SeedLegacyOnlyEventAsync(lineage, legacyRows: 2);

        var migration = await lineage.MigrationAsync();
        var plan = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());

        // Cosmos has no undo. Without somewhere to put the rows, there is no way back.
        await Assert.ThrowsAsync<CosmosTagMigrationNotAuthorizedException>(
            () => migration.ApplyAsync(plan, new CosmosTagMigrationApplyOptions { Confirm = true }));

        Assert.Equal(2, lineage.Tags.Items.Count);
        Assert.Equal(0, lineage.Tags.Deletes);
    }

    [Fact]
    public async Task Applying_Without_A_Plan_Should_Refuse()
    {
        var lineage = NewLineage();
        await SeedLegacyOnlyEventAsync(lineage, legacyRows: 2);

        var migration = await lineage.MigrationAsync();

        // There is no way to delete rows without first producing — and reading — the artifact naming them.
        await Assert.ThrowsAsync<CosmosTagMigrationPlanRejectedException>(
            () => migration.ApplyAsync(null!, Authorized(new RecordingBackupWriter())));

        Assert.Equal(2, lineage.Tags.Items.Count);
    }

    [Fact]
    public async Task Applying_An_Altered_Plan_Should_Refuse()
    {
        var lineage = NewLineage();
        await SeedLegacyOnlyEventAsync(lineage, legacyRows: 2);

        var migration = await lineage.MigrationAsync();
        var plan = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());

        // Someone edits the artifact to widen what it deletes. Its fingerprint no longer matches, so it no
        // longer describes what was reviewed — and an artifact that was not reviewed authorizes nothing.
        var tampered = plan with
        {
            Actions = plan.Actions
                .Select(action => action with
                {
                    RowsToRemove = action.RowsToRemove
                        .Append(new CosmosTagRowRef
                        {
                            Id = "some-other-row",
                            ETag = "etag-1",
                            Snapshot = new CosmosTagRowSnapshot { Id = "some-other-row" }
                        })
                        .ToList()
                })
                .ToList()
        };

        await Assert.ThrowsAsync<CosmosTagMigrationPlanRejectedException>(
            () => migration.ApplyAsync(tampered, Authorized(new RecordingBackupWriter())));

        Assert.Equal(2, lineage.Tags.Items.Count);
        Assert.Equal(0, lineage.Tags.Deletes);
    }

    [Fact]
    public async Task Applying_A_Plan_Built_For_Another_Lineage_Should_Refuse()
    {
        var client = new InMemoryCosmosClient();
        var runtime = NewLineage(ServiceId, client, "runtime-events", "runtime-tags");
        var management = NewLineage(OtherServiceId, client, "management-events", "management-tags");

        await SeedLegacyOnlyEventAsync(runtime, legacyRows: 2);
        await SeedLegacyOnlyEventAsync(management, legacyRows: 2);

        var runtimeMigration = await runtime.MigrationAsync();
        var runtimePlan = await runtimeMigration.PlanAsync(new CosmosTagMigrationPlanOptions());

        var managementMigration = await management.MigrationAsync();

        // Handing one tenant's plan to another tenant's migration is refused, not silently misapplied.
        await Assert.ThrowsAsync<CosmosTagMigrationPlanRejectedException>(
            () => managementMigration.ApplyAsync(runtimePlan, Authorized(new RecordingBackupWriter())));

        Assert.Equal(2, runtime.Tags.Items.Count);
        Assert.Equal(2, management.Tags.Items.Count);
        Assert.Equal(0, management.Tags.Deletes);
    }

    [Fact]
    public async Task A_Failing_Backup_Should_Stop_The_Run_Before_Anything_Is_Deleted()
    {
        var lineage = NewLineage();
        await SeedLegacyOnlyEventAsync(lineage, legacyRows: 2);

        var migration = await lineage.MigrationAsync();
        var plan = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());

        await Assert.ThrowsAsync<IOException>(
            () => migration.ApplyAsync(plan, Authorized(new ThrowingBackupWriter())));

        // The backup is written first precisely so that this is true.
        Assert.Equal(2, lineage.Tags.Items.Count);
        Assert.Equal(0, lineage.Tags.Deletes);
    }

    // ── The reduction itself ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Applying_Should_Reduce_To_The_Canonical_Row_And_Back_Up_First()
    {
        var lineage = NewLineage();
        var written = await SeedLegacyOnlyEventAsync(lineage, legacyRows: 3);

        var migration = await lineage.MigrationAsync();
        var plan = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());

        var backup = new RecordingBackupWriter(() => lineage.Tags.Deletes);
        var report = await migration.ApplyAsync(plan, Authorized(backup));

        // Backed up before a single delete — that is the recovery path.
        Assert.Equal(1, backup.Calls);
        Assert.Equal(0, backup.DeletesWhenCalled);
        Assert.Equal(3, backup.Rows.Count);

        Assert.Equal(1, report.Reduced);
        Assert.Equal(3, report.RowsRemoved);
        Assert.Equal(1, report.SurvivorsCreated);

        // Exactly one row survives, and it is the canonical one the write path would produce.
        var survivor = Assert.Single(lineage.Tags.Items);
        Assert.Equal(written.Id.ToString(), survivor["id"]!.ToString());

        // The audit says what happened to the key, in full.
        var entry = Assert.Single(report.Audit);
        Assert.Equal(CosmosTagMigrationOutcome.Reduced, entry.Outcome);
        Assert.Equal(written.Id.ToString(), entry.SurvivorId);
        Assert.True(entry.SurvivorCreated);
        Assert.Equal(3, entry.RemovedIds.Count);
    }

    [Fact]
    public async Task An_Existing_Canonical_Row_Should_Be_The_Survivor_And_Not_Rewritten()
    {
        var lineage = NewLineage();

        // A normal write lands the canonical row; legacy rows for the same key sit alongside it.
        var written = NewEvent(Tag);
        await lineage.Store.WriteSerializableEventsAsync(new[] { written });
        lineage.Tags.Seed(LegacyRow(ServiceId, Tag, written));
        lineage.Tags.Seed(LegacyRow(ServiceId, Tag, written));

        var createsBefore = lineage.Tags.Creates;

        var migration = await lineage.MigrationAsync();
        var plan = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());

        Assert.True(Assert.Single(plan.Actions).SurvivorExists);

        var report = await migration.ApplyAsync(plan, Authorized(new RecordingBackupWriter()));

        Assert.Equal(1, report.Reduced);
        Assert.Equal(2, report.RowsRemoved);
        Assert.Equal(0, report.SurvivorsCreated);
        Assert.Equal(createsBefore, lineage.Tags.Creates); // the survivor was already there; nothing rewritten

        var survivor = Assert.Single(lineage.Tags.Items);
        Assert.Equal(written.Id.ToString(), survivor["id"]!.ToString());
    }

    [Fact]
    public async Task Re_Running_A_Completed_Migration_Should_Find_Nothing_To_Do()
    {
        var lineage = NewLineage();
        await SeedLegacyOnlyEventAsync(lineage, legacyRows: 2);

        var migration = await lineage.MigrationAsync();
        var plan = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());
        await migration.ApplyAsync(plan, Authorized(new RecordingBackupWriter()));

        var deletesAfterFirst = lineage.Tags.Deletes;

        // The world is already canonical: a fresh plan proposes nothing.
        var replanned = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());

        Assert.Empty(replanned.Actions);
        Assert.Equal(0, replanned.RowsToRemoveCount);
        Assert.Single(lineage.Tags.Items);
        Assert.Equal(deletesAfterFirst, lineage.Tags.Deletes);
    }

    // ── ETag races and staleness ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Row_Changed_Since_The_Plan_Should_Be_Reported_As_Stale_And_Left_Alone()
    {
        var lineage = NewLineage();
        var written = await SeedLegacyOnlyEventAsync(lineage, legacyRows: 2);

        var migration = await lineage.MigrationAsync();
        var plan = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());

        // A concurrent writer touches one of the rows after the plan pinned it. Its ETag moves.
        var target = plan.Actions[0].RowsToRemove[0];
        lineage.Tags.MutateInPlace(
            plan.Actions[0].PartitionKey,
            target.Id,
            row => row["tagGroup"] = "TouchedByeSomeoneElse");

        var report = await migration.ApplyAsync(plan, Authorized(new RecordingBackupWriter()));

        // The plan's authority over the key has lapsed. Nothing is forced.
        Assert.Equal(0, report.Reduced);
        Assert.Equal(0, report.RowsRemoved);
        Assert.Equal(1, report.Stale);
        Assert.Equal(2, lineage.Tags.Items.Count);

        var entry = Assert.Single(report.Audit);
        Assert.Equal(CosmosTagMigrationOutcome.Stale, entry.Outcome);
        Assert.Equal(written.Id, entry.EventId);
    }

    [Fact]
    public async Task A_Row_That_Moves_Between_The_Backup_And_The_Delete_Should_Not_Be_Forced()
    {
        var lineage = NewLineage();
        await SeedLegacyOnlyEventAsync(lineage, legacyRows: 2);

        var migration = await lineage.MigrationAsync();
        var plan = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());
        var partitionKey = plan.Actions[0].PartitionKey;
        var victim = plan.Actions[0].RowsToRemove[1].Id;

        // The row moves after the freshness check, so the ETag-guarded delete is the thing that catches it.
        var backup = new RecordingBackupWriter();
        var racing = new RaceOnBackup(lineage, partitionKey, victim, backup);

        var report = await migration.ApplyAsync(plan, Authorized(racing));

        // Cosmos refused the delete at the pinned version, and the migration did not insist.
        Assert.Equal(1, report.LostRaces);
        Assert.Equal(0, report.Reduced);
        Assert.Contains(lineage.Tags.Items, row => row["id"]!.ToString() == victim);
    }

    /// <summary>Moves a row at the last possible moment: after the backup, before the deletes.</summary>
    private sealed class RaceOnBackup : ICosmosTagMigrationBackupWriter
    {
        private readonly RecordingBackupWriter _inner;
        private readonly Lineage _lineage;
        private readonly string _partitionKey;
        private readonly string _rowId;

        public RaceOnBackup(Lineage lineage, string partitionKey, string rowId, RecordingBackupWriter inner)
        {
            _lineage = lineage;
            _partitionKey = partitionKey;
            _rowId = rowId;
            _inner = inner;
        }

        public async Task WriteAsync(
            CosmosTagMigrationPlan plan,
            IReadOnlyList<CosmosTag> rowsToRemove,
            CancellationToken cancellationToken)
        {
            await _inner.WriteAsync(plan, rowsToRemove, cancellationToken);
            _lineage.Tags.MutateInPlace(_partitionKey, _rowId, row => row["tagGroup"] = "MovedUnderUs");
        }
    }

    // ── The canonical survivor is PROVEN before any delete ──────────────────────────────────────────

    [Fact]
    public async Task A_Survivor_Deleted_Between_Plan_And_Apply_Should_Be_Recreated_Before_Any_Delete()
    {
        var lineage = NewLineage();

        // A normal write lands the canonical row; legacy rows sit alongside it, so the plan sees a survivor.
        var written = NewEvent(Tag);
        await lineage.Store.WriteSerializableEventsAsync(new[] { written });
        lineage.Tags.Seed(LegacyRow(ServiceId, Tag, written));

        var migration = await lineage.MigrationAsync();
        var plan = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());
        Assert.True(Assert.Single(plan.Actions).SurvivorExists);

        // Then it is deleted out from under the plan.
        lineage.Tags.Remove($"{ServiceId}|{Tag}", written.Id.ToString());
        Assert.Single(lineage.Tags.Items); // only the legacy row is left

        var report = await migration.ApplyAsync(plan, Authorized(new RecordingBackupWriter()));

        // The run does not trust the plan: it re-reads, finds the survivor gone, recreates it from the event,
        // and only then removes the legacy row. The key is never left unindexed.
        Assert.Equal(1, report.Reduced);
        Assert.Equal(1, report.SurvivorsCreated);
        Assert.Equal(1, report.RowsRemoved);

        var survivor = Assert.Single(lineage.Tags.Items);
        Assert.Equal(written.Id.ToString(), survivor["id"]!.ToString());
    }

    [Fact]
    public async Task A_Survivor_Changed_Between_Plan_And_Apply_Should_Delete_Nothing()
    {
        var lineage = NewLineage();

        var written = NewEvent(Tag);
        await lineage.Store.WriteSerializableEventsAsync(new[] { written });
        lineage.Tags.Seed(LegacyRow(ServiceId, Tag, written));
        lineage.Tags.Seed(LegacyRow(ServiceId, Tag, written));

        var migration = await lineage.MigrationAsync();
        var plan = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());

        // The canonical row is corrupted after the plan was reviewed. Deleting the legacy rows now would
        // leave the key indexed by a row that disagrees with its event — worse than leaving the duplicates.
        lineage.Tags.MutateInPlace(
            $"{ServiceId}|{Tag}",
            written.Id.ToString(),
            row => row["eventType"] = "SomethingElse");

        var report = await migration.ApplyAsync(plan, Authorized(new RecordingBackupWriter()));

        Assert.Equal(0, report.Reduced);
        Assert.Equal(0, report.RowsRemoved);
        Assert.Equal(1, report.StaleSurvivors);
        Assert.Equal(0, lineage.Tags.Deletes);
        Assert.Equal(3, lineage.Tags.Items.Count); // corrupted survivor + both legacy rows, all intact

        var entry = Assert.Single(report.Audit);
        Assert.Equal(CosmosTagMigrationOutcome.StaleSurvivor, entry.Outcome);
    }

    [Fact]
    public async Task A_Legacy_Row_Whose_CreatedAt_Moved_Should_Not_Be_Deleted()
    {
        var lineage = NewLineage();
        await SeedLegacyOnlyEventAsync(lineage, legacyRows: 1);

        var migration = await lineage.MigrationAsync();
        var plan = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());
        var partitionKey = plan.Actions[0].PartitionKey;
        var victim = plan.Actions[0].RowsToRemove[0].Id;

        // Only createdAt moves — a field the legacy comparator ignores by design, because a legacy row is
        // ALLOWED to have a wall-clock createdAt. But this is not a classification question: the row is not
        // what the operator reviewed, so it does not get deleted.
        var backup = new MutateOnBackup(
            lineage,
            partitionKey,
            victim,
            row => row["createdAt"] = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        var report = await migration.ApplyAsync(plan, Authorized(backup));

        Assert.Equal(1, report.LostRaces);
        Assert.Equal(0, report.RowsRemoved);
        Assert.Contains(lineage.Tags.Items, row => row["id"]!.ToString() == victim);
    }

    /// <summary>Rewrites a row at the last possible moment: after the backup, before the deletes.</summary>
    private sealed class MutateOnBackup : ICosmosTagMigrationBackupWriter
    {
        private readonly Lineage _lineage;
        private readonly Action<Newtonsoft.Json.Linq.JObject> _mutate;
        private readonly string _partitionKey;
        private readonly string _rowId;

        public MutateOnBackup(
            Lineage lineage,
            string partitionKey,
            string rowId,
            Action<Newtonsoft.Json.Linq.JObject> mutate)
        {
            _lineage = lineage;
            _partitionKey = partitionKey;
            _rowId = rowId;
            _mutate = mutate;
        }

        public Task WriteAsync(
            CosmosTagMigrationPlan plan,
            IReadOnlyList<CosmosTag> rowsToRemove,
            CancellationToken cancellationToken)
        {
            _lineage.Tags.MutateInPlace(_partitionKey, _rowId, _mutate);
            return Task.CompletedTask;
        }
    }

    // ── The fingerprint cannot be fooled ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Tampering_With_A_Plans_Tag_Should_Invalidate_Its_Fingerprint()
    {
        var lineage = NewLineage();
        await SeedLegacyOnlyEventAsync(lineage, legacyRows: 1);

        var migration = await lineage.MigrationAsync();
        var plan = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());

        // The tag is what decides which partition gets touched. If it were not fingerprinted, an edited
        // artifact could point the deletion at a different partition and still look authentic.
        var tampered = plan with
        {
            Actions = plan.Actions.Select(action => action with { Tag = "Student:999" }).ToList()
        };

        await Assert.ThrowsAsync<CosmosTagMigrationPlanRejectedException>(
            () => migration.ApplyAsync(tampered, Authorized(new RecordingBackupWriter())));

        Assert.Equal(0, lineage.Tags.Deletes);
    }

    [Fact]
    public void The_Fingerprint_Should_Not_Collide_Across_A_Field_Boundary()
    {
        // Concatenating fields with a separator is ambiguous the moment a value can contain that separator.
        // These two plans are DIFFERENT — they name different partitions and different tags — but under
        // naive `value + ";"` concatenation their fingerprint inputs are the same string:
        //
        //   left  : partitionKey "svc|Student:1"    tag "a;b"   ->  "svc|Student:1;" + "a;b;"
        //   right : partitionKey "svc|Student:1;a"  tag "b"     ->  "svc|Student:1;a;" + "b;"
        //
        //   both  : "svc|Student:1;a;b;"
        //
        // A fingerprint that cannot tell those apart would authenticate an artifact that deletes from the
        // wrong partition. Length-prefixing every field removes the ambiguity: there is nothing to collide.
        var left = new CosmosTagMigrationPlan
        {
            ServiceId = "svc",
            EventsContainer = "events",
            TagsContainer = "tags",
            Actions = new[]
            {
                new CosmosTagMigrationAction
                {
                    PartitionKey = "svc|Student:1",
                    Tag = "a;b",
                    EventId = Guid.Empty,
                    SurvivorId = "survivor",
                    SurvivorExpected = new CosmosTagRowSnapshot()
                }
            }
        };

        var right = left with
        {
            Actions = new[]
            {
                left.Actions[0] with
                {
                    PartitionKey = "svc|Student:1;a",
                    Tag = "b"
                }
            }
        };

        // Different plans, different fingerprints — as they must be.
        Assert.NotEqual(left.ComputeFingerprint(), right.ComputeFingerprint());
    }

    // ── Corruption and overflow are never deleted ───────────────────────────────────────────────────

    [Fact]
    public async Task A_Row_That_Disagrees_With_Its_Event_Should_Be_Skipped_Not_Deleted()
    {
        var lineage = NewLineage();
        var written = await SeedLegacyOnlyEventAsync(lineage, legacyRows: 1);

        // A row that indexes the key but claims a different event type is not a duplicate — it is corruption,
        // and deciding what to do about corruption is not this service's call.
        var corrupt = LegacyRow(ServiceId, Tag, written);
        corrupt.EventType = "SomethingElse";
        lineage.Tags.Seed(corrupt);

        var migration = await lineage.MigrationAsync();
        var plan = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());

        Assert.Empty(plan.Actions);
        var skip = Assert.Single(plan.Skipped);
        Assert.Equal(CosmosTagMigrationSkipReason.Corrupt, skip.Reason);

        var report = await migration.ApplyAsync(plan, Authorized(new RecordingBackupWriter()));

        Assert.Equal(0, report.RowsRemoved);
        Assert.Equal(1, report.Skipped);
        Assert.Equal(2, lineage.Tags.Items.Count);
        Assert.Equal(0, lineage.Tags.Deletes);
    }

    [Fact]
    public async Task More_Rows_Than_The_Cap_Should_Be_Reported_As_Overflow_Not_Deleted()
    {
        var lineage = NewLineage();
        await SeedLegacyOnlyEventAsync(lineage, legacyRows: 5);

        var migration = await lineage.MigrationAsync();
        var plan = await migration.PlanAsync(new CosmosTagMigrationPlanOptions { MaxRowsPerKey = 2 });

        Assert.Empty(plan.Actions);
        Assert.Equal(CosmosTagMigrationSkipReason.Overflow, Assert.Single(plan.Skipped).Reason);

        var report = await migration.ApplyAsync(plan, Authorized(new RecordingBackupWriter()));

        Assert.Equal(0, report.RowsRemoved);
        Assert.Equal(5, lineage.Tags.Items.Count);
    }

    // ── Bounded plans resume ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Bounded_Plan_Should_Resume_From_Its_Checkpoint()
    {
        var lineage = NewLineage();
        for (var i = 0; i < 4; i++)
        {
            await SeedLegacyOnlyEventAsync(lineage, legacyRows: 1);
        }

        var migration = await lineage.MigrationAsync();

        var first = await migration.PlanAsync(new CosmosTagMigrationPlanOptions
        {
            MaxEventsToScan = 2,
            PageSize = 2
        });

        Assert.Equal(2, first.EventsScanned);
        Assert.Equal(2, first.Actions.Count);
        Assert.True(first.HasMore);
        Assert.NotNull(first.Checkpoint);

        var second = await migration.PlanAsync(new CosmosTagMigrationPlanOptions
        {
            Checkpoint = first.Checkpoint
        });

        Assert.Equal(2, second.EventsScanned);
        Assert.Equal(2, second.Actions.Count);
        Assert.False(second.HasMore);

        // Applying both plans reduces all four keys, and no key is touched twice.
        await migration.ApplyAsync(first, Authorized(new RecordingBackupWriter()));
        await migration.ApplyAsync(second, Authorized(new RecordingBackupWriter()));

        Assert.Equal(4, lineage.Tags.Items.Count); // one canonical row per event
        Assert.Equal(4, lineage.Tags.Deletes);
    }

    // ── Lineage isolation ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Migration_Should_Not_Touch_Another_Lineage()
    {
        var client = new InMemoryCosmosClient();
        var runtime = NewLineage(ServiceId, client, "runtime-events", "runtime-tags");
        var management = NewLineage(OtherServiceId, client, "management-events", "management-tags");

        await SeedLegacyOnlyEventAsync(runtime, legacyRows: 2);
        await SeedLegacyOnlyEventAsync(management, legacyRows: 2);

        var migration = await runtime.MigrationAsync();
        var plan = await migration.PlanAsync(new CosmosTagMigrationPlanOptions());
        await migration.ApplyAsync(plan, Authorized(new RecordingBackupWriter()));

        Assert.Single(runtime.Tags.Items);
        Assert.Equal(2, runtime.Tags.Deletes);

        // The other lineage is not merely un-migrated — it was never written to.
        Assert.Equal(2, management.Tags.Items.Count);
        Assert.Equal(0, management.Tags.Deletes);
    }

    // ── The sweep cannot reach any of this ──────────────────────────────────────────────────────────

    [Fact]
    public void The_Automatic_Sweep_Should_Have_No_Route_To_The_Destructive_Surface()
    {
        // The sweep runs unattended. If it could reach a delete — by configuration, by DI, by a shared type —
        // every guarantee above would be worth nothing. So assert it structurally.
        var forbidden = new[]
        {
            typeof(CosmosDbLegacyTagMigrationService),
            typeof(CosmosDbLegacyTagMigrationServiceFactory),
            typeof(CosmosTagMigrationApplyOptions),
            typeof(CosmosTagMigrationPlan)
        };

        foreach (var reachable in new[]
        {
            typeof(CosmosTagSweepService),
            typeof(CosmosTagSweepOptions),
            typeof(CosmosDbTagRepairService),
            typeof(CosmosTagRepairOptions)
        })
        {
            var mentioned = reachable
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType))
                .Concat(reachable
                    .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(field => field.FieldType))
                .Concat(reachable
                    .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Select(property => property.PropertyType))
                .Concat(reachable
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .SelectMany(method => method.GetParameters()
                        .Select(parameter => parameter.ParameterType)
                        .Append(method.ReturnType)))
                .ToList();

            foreach (var destructive in forbidden)
            {
                Assert.DoesNotContain(destructive, mentioned);
            }
        }
    }

    [Fact]
    public void The_Sweeps_Store_Seam_Should_Still_Be_Unable_To_Express_A_Delete()
    {
        // The repair store — the sweep's only route to storage — has no delete, and the migration's store is
        // a different seam entirely. That separation is the whole structural argument.
        var repairMembers = typeof(ICosmosTagRepairStore)
            .GetMethods()
            .Select(method => method.Name)
            .ToList();

        Assert.DoesNotContain(repairMembers, name => name.Contains("Delete", StringComparison.OrdinalIgnoreCase));

        var migrationStore = typeof(CosmosDbLegacyTagMigrationService).Assembly
            .GetType("Sekiban.Dcb.CosmosDb.Migration.ICosmosTagMigrationStore");

        Assert.NotNull(migrationStore);
        Assert.Contains(
            migrationStore!.GetMethods(),
            method => method.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase));

        // And it is internal: no assembly outside the provider — the operator CLI included — can implement it
        // or call it. The only public way to delete a tag row is ApplyAsync, behind every gate above.
        Assert.False(migrationStore.IsPublic);
    }

    [Fact]
    public void AddSekibanDcbCosmosDb_Alone_Should_Not_Register_The_Destructive_Migration()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbCosmosDb("AccountEndpoint=https://localhost:8081/;AccountKey=key==", "db");
        services.AddSekibanDcbCosmosDbTagRepair();
        services.AddSekibanDcbCosmosDbTagSweep();

        // Even an application that wants repair AND the automatic sweep does not get the ability to delete.
        Assert.Null(services.BuildServiceProvider().GetService<CosmosDbLegacyTagMigrationServiceFactory>());
    }

    [Fact]
    public void The_Destructive_Migration_Should_Be_Registrable_Only_On_Purpose()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbCosmosDb("AccountEndpoint=https://localhost:8081/;AccountKey=key==", "db");
        services.AddSekibanDcbCosmosDbLegacyTagMigration();

        Assert.NotNull(services.BuildServiceProvider().GetRequiredService<CosmosDbLegacyTagMigrationServiceFactory>());
    }
}
