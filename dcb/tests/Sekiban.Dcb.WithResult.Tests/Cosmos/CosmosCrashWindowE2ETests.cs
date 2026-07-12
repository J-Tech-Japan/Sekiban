using Dcb.Domain;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.CosmosDb.Repair;
using Sekiban.Dcb.CosmosDb.Sweep;
using Sekiban.Dcb.CosmosDb.Tags;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Tests.Cosmos;

/// <summary>
///     The crash-window matrix: crash → restart → repair → convergence, driven end to end through the real
///     <see cref="CosmosDbEventStore" />, the real <see cref="CosmosDbTagRepairService" />, and the real
///     sweep, against in-memory Cosmos containers.
///     The scoped tests that landed with the earlier slices check each piece in isolation. These check the
///     thing those pieces exist for: that after a crash in the two-phase write, a tag-scoped reader
///     eventually sees the event — and that nothing stronger than "eventually" is true, because that is all
///     the documentation claims.
///     Everything here is deterministic. Faults are injected through the seams; nothing sleeps, and nothing
///     depends on timing.
/// </summary>
public class CosmosCrashWindowE2ETests
{
    private const string ServiceId = "svc";
    private const string OtherServiceId = "other";

    /// <summary>One service's containers plus the store that writes to them.</summary>
    private sealed class Lineage
    {
        public Lineage(string serviceId, InMemoryCosmosClient client, CosmosDbEventStoreOptions options)
        {
            ServiceId = serviceId;
            Client = client;
            Options = options;

            var context = new CosmosDbContext(client, "test-db", null, options);
            var resolver = new DefaultCosmosContainerResolver(options);
            Store = new CosmosDbEventStore(
                context,
                DomainType.GetDomainTypes().EventTypes,
                new FixedServiceIdProvider(serviceId),
                resolver);
            RepairFactory = new CosmosDbTagRepairServiceFactory(context, resolver);
        }

        public string ServiceId { get; }
        public InMemoryCosmosClient Client { get; }
        public CosmosDbEventStoreOptions Options { get; }
        public CosmosDbEventStore Store { get; }
        public CosmosDbTagRepairServiceFactory RepairFactory { get; }

        public InMemoryCosmosContainer Events => Client.Container(Options.EventsContainerName);
        public InMemoryCosmosContainer Tags => Client.Container(Options.TagsContainerName);

        public Task<CosmosDbTagRepairService> RepairAsync() => RepairFactory.CreateAsync(ServiceId);
    }

    private sealed class FixedServiceIdProvider : IServiceIdProvider
    {
        private readonly string _serviceId;

        public FixedServiceIdProvider(string serviceId) => _serviceId = serviceId;

        public string GetCurrentServiceId() => _serviceId;
    }

    /// <summary>Fails the tag write before batch N, deterministically — the seam, not a race.</summary>
    private sealed class FailBeforeBatch : ICosmosTagWriteFaultInjector
    {
        private readonly int _batchIndex;

        public FailBeforeBatch(int batchIndex) => _batchIndex = batchIndex;

        public Task OnBeforeBatchAsync(int batchIndex, string partitionKey, IReadOnlyList<CosmosTag> rows)
        {
            if (batchIndex >= _batchIndex)
            {
                throw new InvalidOperationException($"Injected crash before tag batch {batchIndex}");
            }

            return Task.CompletedTask;
        }
    }

    private static CosmosDbEventStoreOptions NewOptions() =>
        new()
        {
            EventsContainerName = "events",
            TagsContainerName = "tags",
            // Roll-forward with a single attempt reproduces exactly what a crash leaves behind: the events
            // are durable and nothing is deleted, but the tag write never completed.
            WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward,
            TagWriteRetry = new CosmosTagWriteRetryOptions { MaxAttempts = 1, JitterRatio = 0 }
        };

    private static Lineage NewLineage(string serviceId = ServiceId, CosmosDbEventStoreOptions? options = null) =>
        new(serviceId, new InMemoryCosmosClient(), options ?? NewOptions());

    private static SerializableEvent NewEvent(params string[] tags) =>
        new(
            System.Text.Encoding.UTF8.GetBytes("""{"Name":"test"}"""),
            SortableUniqueId.GenerateNew(),
            Guid.NewGuid(),
            new EventMetadata("causation", "correlation", "user"),
            tags.ToList(),
            "TestEventPayload");

    private sealed record TestTag(string Value) : ITag
    {
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => Value.Split(':')[0];
        public string GetTag() => Value;
        public string GetTagContent() => Value.Split(':')[1];
    }

    private static async Task<CosmosTagRepairReport> RunRepairAsync(Lineage lineage, bool dryRun = false)
    {
        var repair = await lineage.RepairAsync();
        return await repair.RepairAsync(new CosmosTagRepairOptions { DryRun = dryRun });
    }

    // ── Scenario 1: crash before any tag batch ──────────────────────────────────────────────────────

    [Fact]
    public async Task Crash_Before_Any_Tag_Batch_Should_Converge_After_Repair()
    {
        var lineage = NewLineage();
        lineage.Store.TagWriteFaultInjector = new FailBeforeBatch(0); // die before the first batch

        var written = NewEvent("Student:1");
        var result = await lineage.Store.WriteSerializableEventsAsync(new[] { written });

        // The crash residue: the event is durable and was NOT deleted, but no tag row landed.
        Assert.False(result.IsSuccess);
        Assert.IsType<CosmosTagWriteExhaustedException>(result.GetException());
        Assert.Single(lineage.Events.Items);
        Assert.Empty(lineage.Tags.Items);
        Assert.Equal(0, lineage.Events.Deletes);

        // An all-events reader sees it; a tag-scoped reader does not. This is the window.
        var all = await lineage.Store.ReadAllSerializableEventsAsync();
        Assert.Single(all.GetValue());

        var byTagBefore = await lineage.Store.ReadSerializableEventsByTagAsync(new TestTag("Student:1"));
        Assert.Empty(byTagBefore.GetValue());

        // Restart: the fault is gone, and repair runs.
        lineage.Store.TagWriteFaultInjector = null;
        var report = await RunRepairAsync(lineage);

        Assert.Equal(1, report.Missing);
        Assert.Equal(1, report.Repaired);

        // The window is closed: the tag-scoped reader now sees the event.
        var byTagAfter = await lineage.Store.ReadSerializableEventsByTagAsync(new TestTag("Student:1"));
        Assert.Equal(written.Id, Assert.Single(byTagAfter.GetValue()).Id);
    }

    // ── Scenario 2: crash mid tag batches ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Crash_Between_Tag_Batches_Should_Converge_Without_Duplicates()
    {
        var lineage = NewLineage();

        // Three tags = three tag partitions = three batches. Die before the second, so the first landed.
        lineage.Store.TagWriteFaultInjector = new FailBeforeBatch(1);

        var written = NewEvent("Student:1", "Student:2", "Student:3");
        var result = await lineage.Store.WriteSerializableEventsAsync(new[] { written });

        Assert.False(result.IsSuccess);
        Assert.Single(lineage.Events.Items);
        Assert.Single(lineage.Tags.Items); // exactly one of the three tag rows landed

        lineage.Store.TagWriteFaultInjector = null;
        var report = await RunRepairAsync(lineage);

        // The row that landed is recognized, not rewritten; the two that did not are backfilled.
        Assert.Equal(1, report.Present);
        Assert.Equal(2, report.Missing);
        Assert.Equal(2, report.Repaired);
        Assert.Equal(3, lineage.Tags.Items.Count);

        foreach (var tag in new[] { "Student:1", "Student:2", "Student:3" })
        {
            var byTag = await lineage.Store.ReadSerializableEventsByTagAsync(new TestTag(tag));
            Assert.Equal(written.Id, Assert.Single(byTag.GetValue()).Id);
        }
    }

    // ── Scenario 3: multi-event partial create ──────────────────────────────────────────────────────

    [Fact]
    public async Task A_Partially_Failed_Multi_Event_Write_Should_Delete_Nothing_And_Name_Both_Sets()
    {
        var lineage = NewLineage();

        // The second event's create fails. Events have distributed partition keys, so no transaction spans
        // them: whichever ones landed are already visible.
        lineage.Events.WriteFaults.Enqueue(CosmosFailures.Conflict());

        var events = new[] { NewEvent("Student:1"), NewEvent("Student:2") };
        var result = await lineage.Store.WriteSerializableEventsAsync(events);

        Assert.False(result.IsSuccess);
        var failure = Assert.IsType<CosmosPartialEventWriteException>(result.GetException());

        Assert.Single(failure.FailedEventIds);
        Assert.Single(failure.WrittenEventIds);

        // Nothing is deleted — a multi-projection may already have read the event that landed.
        Assert.Equal(0, lineage.Events.Deletes);
        Assert.Single(lineage.Events.Items);

        // And the event that never landed is not indexed either.
        Assert.Empty(lineage.Tags.Items);
    }

    // ── Scenario 4: re-execution and corruption, through the real store ─────────────────────────────

    /// <summary>Fails the tag write's first <c>failures</c> batch attempts, then lets it through.</summary>
    private sealed class FailFirstAttempts : ICosmosTagWriteFaultInjector
    {
        private int _remaining;

        public FailFirstAttempts(int failures) => _remaining = failures;

        public Task OnBeforeBatchAsync(int batchIndex, string partitionKey, IReadOnlyList<CosmosTag> rows)
        {
            if (_remaining-- > 0)
            {
                throw new InvalidOperationException("Injected transient tag-write failure");
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>Retries without waiting, so a backoff policy costs a test no wall-clock.</summary>
    private sealed class NoWaitRetryScheduler : ICosmosRetryScheduler
    {
        public DateTimeOffset UtcNow => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
        public double NextJitter() => 0;
    }

    [Fact]
    public async Task Roll_Forward_Should_Converge_After_A_Transient_Tag_Failure_Without_Duplicates()
    {
        var options = NewOptions();
        options.TagWriteRetry = new CosmosTagWriteRetryOptions { MaxAttempts = 3, JitterRatio = 0 };

        var lineage = NewLineage(options: options);
        lineage.Store.RetryScheduler = new NoWaitRetryScheduler();

        // The first attempt dies partway through the tag batches; the retry re-executes the whole stage.
        lineage.Store.TagWriteFaultInjector = new FailFirstAttempts(1);

        var written = NewEvent("Student:1", "Student:2", "Student:3");
        var result = await lineage.Store.WriteSerializableEventsAsync(new[] { written });

        // Re-executing a tag write is safe because the rows derive deterministically: the retry re-derives
        // identical rows, accepts the one the failed attempt already created, and fills in the rest.
        Assert.True(result.IsSuccess);
        Assert.Equal(3, lineage.Tags.Items.Count);
        Assert.Equal(0, lineage.Events.Deletes);

        foreach (var tag in new[] { "Student:1", "Student:2", "Student:3" })
        {
            var byTag = await lineage.Store.ReadSerializableEventsByTagAsync(new TestTag(tag));
            Assert.Equal(written.Id, Assert.Single(byTag.GetValue()).Id);
        }
    }

    [Fact]
    public async Task Re_Executing_The_Same_Write_Should_Not_Duplicate_Tag_Rows()
    {
        var lineage = NewLineage();
        var written = NewEvent("Student:1");

        var first = await lineage.Store.WriteSerializableEventsAsync(new[] { written });
        Assert.True(first.IsSuccess);
        Assert.Single(lineage.Tags.Items);

        // Replaying the same event: the event create conflicts, but the tag rows must not multiply.
        var second = await lineage.Store.WriteSerializableEventsAsync(new[] { written });
        Assert.False(second.IsSuccess); // the event already exists

        Assert.Single(lineage.Tags.Items);
        var byTag = await lineage.Store.ReadSerializableEventsByTagAsync(new TestTag("Student:1"));
        Assert.Single(byTag.GetValue());
    }

    [Fact]
    public async Task A_Tag_Row_That_Disagrees_With_Its_Event_Should_Be_Reported_As_Corruption()
    {
        var lineage = NewLineage();
        var written = NewEvent("Student:1");
        await lineage.Store.WriteSerializableEventsAsync(new[] { written });

        // Something outside Sekiban rewrote the row's event type.
        var corrupted = CosmosTagIdentity.DeriveRow(
            ServiceId,
            "Student:1",
            written.Id,
            written.SortableUniqueIdValue,
            "SomethingElse");
        lineage.Tags.Seed(corrupted);

        var report = await RunRepairAsync(lineage);

        // Reported, never overwritten.
        Assert.Equal(1, report.Corrupt);
        Assert.Equal(0, report.Repaired);
        Assert.Equal("SomethingElse", Assert.Single(lineage.Tags.Items)["eventType"]!.ToString());
    }

    // ── Scenario 5: repair re-run and concurrency ───────────────────────────────────────────────────

    [Fact]
    public async Task Repairs_Should_Be_Idempotent_When_Re_Run_And_When_Run_Concurrently()
    {
        var lineage = NewLineage();
        lineage.Store.TagWriteFaultInjector = new FailBeforeBatch(0);
        await lineage.Store.WriteSerializableEventsAsync(new[] { NewEvent("Student:1") });
        lineage.Store.TagWriteFaultInjector = null;

        // Two repairs racing each other over the same residue.
        var concurrent = await Task.WhenAll(RunRepairAsync(lineage), RunRepairAsync(lineage));

        // Exactly one of them writes the row; the other finds it already there. Between the two, the pair is
        // accounted for exactly once, and neither reports corruption.
        Assert.Equal(1, concurrent.Sum(report => report.Repaired));
        Assert.Equal(1, concurrent.Sum(report => report.Present));
        Assert.Single(lineage.Tags.Items);
        Assert.All(concurrent, report => Assert.Equal(0, report.Corrupt));

        // A third, after the fact, finds nothing to do.
        var again = await RunRepairAsync(lineage);
        Assert.Equal(0, again.Repaired);
        Assert.Equal(1, again.Present);
        Assert.Single(lineage.Tags.Items);
    }

    // ── Scenario 6: checkpoint/resume under throttling and cancellation ─────────────────────────────

    [Fact]
    public async Task A_Scan_Should_Resume_Across_Runs_And_Survive_Throttling()
    {
        var lineage = NewLineage();

        // Six events whose tag rows never landed.
        lineage.Store.TagWriteFaultInjector = new FailBeforeBatch(0);
        for (var i = 0; i < 6; i++)
        {
            await lineage.Store.WriteSerializableEventsAsync(new[] { NewEvent($"Student:{i}") });
        }

        lineage.Store.TagWriteFaultInjector = null;
        Assert.Empty(lineage.Tags.Items);

        // The tags container throttles the first write. Retry-After is honored, not shortened.
        lineage.Tags.WriteFaults.Enqueue(CosmosFailures.Throttled(TimeSpan.FromMilliseconds(1)));

        var repair = await lineage.RepairAsync();

        var first = await repair.RepairAsync(new CosmosTagRepairOptions
        {
            DryRun = false,
            MaxEventsToScan = 4,
            PageSize = 2
        });

        Assert.Equal(4, first.EventsScanned);
        Assert.Equal(4, first.Repaired);
        Assert.True(first.HasMore);
        Assert.NotNull(first.Checkpoint);

        var second = await repair.RepairAsync(new CosmosTagRepairOptions
        {
            DryRun = false,
            Checkpoint = first.Checkpoint
        });

        // Resuming picks up exactly the two it had not reached — no gap, no re-repair.
        Assert.Equal(2, second.EventsScanned);
        Assert.Equal(2, second.Repaired);
        Assert.False(second.HasMore);
        Assert.Equal(6, lineage.Tags.Items.Count);
    }

    [Fact]
    public async Task A_Cancelled_Scan_Should_Keep_The_Progress_It_Settled()
    {
        var lineage = NewLineage();
        lineage.Store.TagWriteFaultInjector = new FailBeforeBatch(0);
        for (var i = 0; i < 4; i++)
        {
            await lineage.Store.WriteSerializableEventsAsync(new[] { NewEvent($"Student:{i}") });
        }

        lineage.Store.TagWriteFaultInjector = null;

        using var cts = new CancellationTokenSource();
        var repair = await lineage.RepairAsync();

        // Cancel exactly as the third tag row is about to be written, as a run budget elapsing would — at a
        // precise point, not whenever a polling loop happens to notice.
        lineage.Tags.OnWrite = alreadyWritten =>
        {
            if (alreadyWritten == 2)
            {
                cts.Cancel();
            }
        };

        var exception = await Assert.ThrowsAsync<CosmosTagRepairCancelledException>(
            () => repair.RepairAsync(
                new CosmosTagRepairOptions { DryRun = false, PageSize = 1 },
                cts.Token));

        lineage.Tags.OnWrite = null;

        // Cancelling does not abandon the row already in flight — it completes, and the run stops at the next
        // check. So three events are settled, and the report says exactly three: the checkpoint never claims
        // more progress than was actually made, nor less.
        Assert.Equal(3, exception.PartialReport.Repaired);
        Assert.Equal(3, exception.PartialReport.EventsScanned);
        Assert.Equal(3, lineage.Tags.Items.Count);
        Assert.NotNull(exception.PartialReport.Checkpoint);

        // Resuming from the cancelled run's checkpoint finishes the one it did not reach, and redoes none of
        // the three it did.
        var resumed = await repair.RepairAsync(new CosmosTagRepairOptions
        {
            DryRun = false,
            Checkpoint = exception.PartialReport.Checkpoint
        });

        Assert.Equal(1, resumed.EventsScanned);
        Assert.Equal(1, resumed.Repaired);
        Assert.Equal(0, resumed.Corrupt);
        Assert.Equal(4, lineage.Tags.Items.Count);
    }

    // ── Scenario 7: readers racing repair — the documented non-guarantee ────────────────────────────

    [Fact]
    public async Task A_Tag_Reader_Racing_Repair_Sees_Eventual_Repair_And_Nothing_Stronger()
    {
        var lineage = NewLineage();
        lineage.Store.TagWriteFaultInjector = new FailBeforeBatch(0);
        var written = NewEvent("Student:1");
        await lineage.Store.WriteSerializableEventsAsync(new[] { written });
        lineage.Store.TagWriteFaultInjector = null;

        var tag = new TestTag("Student:1");

        // Before repair, a tag reader does NOT see the event, and the tag-consistent baseline is regressed
        // with it. Nothing gates the reader — this is the documented window, asserted rather than wished away.
        Assert.Empty((await lineage.Store.ReadSerializableEventsByTagAsync(tag)).GetValue());
        Assert.False((await lineage.Store.TagExistsAsync(tag)).GetValue());
        Assert.Equal(string.Empty, (await lineage.Store.GetLatestTagAsync(tag)).GetValue().LastSortedUniqueId);

        // Meanwhile the event is durable and visible to all-events readers the whole time.
        Assert.Single((await lineage.Store.ReadAllSerializableEventsAsync()).GetValue());

        await RunRepairAsync(lineage);

        // Only after repair does the tag reader converge. "Eventually" — never "before the read".
        Assert.Single((await lineage.Store.ReadSerializableEventsByTagAsync(tag)).GetValue());
        Assert.True((await lineage.Store.TagExistsAsync(tag)).GetValue());
        Assert.Equal(
            written.SortableUniqueIdValue,
            (await lineage.Store.GetLatestTagAsync(tag)).GetValue().LastSortedUniqueId);
    }

    // ── Consumer contract: both registration styles ─────────────────────────────────────────────────

    [Fact]
    public void Both_Registration_Styles_Should_Reach_The_Same_Non_Destructive_Surface()
    {
        // DI style.
        var services = new ServiceCollection();
        services.AddSekibanDcbCosmosDb("AccountEndpoint=https://localhost:8081/;AccountKey=key==", "db");
        services.AddSekibanDcbCosmosDbTagRepair();
        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<CosmosDbTagRepairServiceFactory>());

        // Manual style: options + context + resolver, no container.
        var options = new CosmosDbEventStoreOptions { EventsContainerName = "events", TagsContainerName = "tags" };
        var manual = new CosmosDbTagRepairServiceFactory(
            new CosmosDbContext(new InMemoryCosmosClient(), "db", null, options),
            new DefaultCosmosContainerResolver(options));

        Assert.NotNull(manual);
    }

    [Fact]
    public async Task The_Manual_Construction_Path_Should_Repair_Exactly_Like_The_DI_Path()
    {
        // The manual path is what SekibanWasmRuntime / SekibanAsAService actually use, so it gets the same
        // end-to-end proof, not just a smoke check that it constructs.
        var lineage = NewLineage();
        lineage.Store.TagWriteFaultInjector = new FailBeforeBatch(0);
        await lineage.Store.WriteSerializableEventsAsync(new[] { NewEvent("Student:1") });
        lineage.Store.TagWriteFaultInjector = null;

        var report = await RunRepairAsync(lineage);

        Assert.Equal(1, report.Repaired);
        Assert.Single(lineage.Tags.Items);
    }

    // ── Compatibility: legacy defaults do nothing new ───────────────────────────────────────────────

    [Fact]
    public async Task Legacy_Defaults_Should_Behave_As_Before_The_Upgrade()
    {
        // Exactly what a downstream repo constructs today: container names, nothing else.
        var options = new CosmosDbEventStoreOptions
        {
            EventsContainerName = "events",
            TagsContainerName = "tags"
        };

        Assert.Equal(CosmosWriteFailurePolicy.Compatible, options.WriteFailurePolicy);

        var lineage = NewLineage(options: options);
        lineage.Store.TagWriteFaultInjector = new FailBeforeBatch(0);

        var result = await lineage.Store.WriteSerializableEventsAsync(new[] { NewEvent("Student:1") });

        // The pre-upgrade behavior, unchanged: no retry, and the legacy rollback deletes the written event.
        Assert.False(result.IsSuccess);
        Assert.IsType<InvalidOperationException>(result.GetException());
        Assert.Equal(1, lineage.Events.Deletes);
        Assert.Empty(lineage.Events.Items);
    }

    [Fact]
    public void A_Disabled_Sweep_Should_Add_No_Background_Work()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbCosmosDb("AccountEndpoint=https://localhost:8081/;AccountKey=key==", "db");

        // Upgrading the package adds no sweep, no hosted service, no configuration to fill in.
        Assert.Null(services.BuildServiceProvider().GetService<CosmosTagSweepOptions>());
        Assert.False(new CosmosTagSweepOptions().Enabled);
    }

    // ── Two-lineage isolation ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Repair_Should_Not_Cross_Lineages()
    {
        // Two services sharing one Cosmos account but different container lineages — the management/runtime
        // split a multi-tenant host has.
        var client = new InMemoryCosmosClient();
        var runtime = new Lineage(
            ServiceId,
            client,
            new CosmosDbEventStoreOptions
            {
                EventsContainerName = "runtime-events",
                TagsContainerName = "runtime-tags",
                WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward,
                TagWriteRetry = new CosmosTagWriteRetryOptions { MaxAttempts = 1 }
            });

        var management = new Lineage(
            OtherServiceId,
            client,
            new CosmosDbEventStoreOptions
            {
                EventsContainerName = "management-events",
                TagsContainerName = "management-tags",
                WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward,
                TagWriteRetry = new CosmosTagWriteRetryOptions { MaxAttempts = 1 }
            });

        // Both crash with residue.
        runtime.Store.TagWriteFaultInjector = new FailBeforeBatch(0);
        management.Store.TagWriteFaultInjector = new FailBeforeBatch(0);
        await runtime.Store.WriteSerializableEventsAsync(new[] { NewEvent("Student:1") });
        await management.Store.WriteSerializableEventsAsync(new[] { NewEvent("Student:1") });
        runtime.Store.TagWriteFaultInjector = null;
        management.Store.TagWriteFaultInjector = null;

        // Repair only the runtime lineage.
        var report = await RunRepairAsync(runtime);

        Assert.Equal(1, report.Repaired);
        Assert.Single(runtime.Tags.Items);

        // The management lineage is untouched — an instance is bound to one lineage at construction, so it
        // cannot reach across even by accident.
        Assert.Empty(management.Tags.Items);
        Assert.Equal(0, management.Tags.Creates);

        // And repairing it separately fixes only it.
        var managementReport = await RunRepairAsync(management);
        Assert.Equal(1, managementReport.Repaired);
        Assert.Single(management.Tags.Items);
        Assert.Single(runtime.Tags.Items);
    }

    [Fact]
    public async Task A_Dry_Run_Should_Report_The_Crash_Residue_Without_Repairing_It()
    {
        var lineage = NewLineage();
        lineage.Store.TagWriteFaultInjector = new FailBeforeBatch(0);
        await lineage.Store.WriteSerializableEventsAsync(new[] { NewEvent("Student:1") });
        lineage.Store.TagWriteFaultInjector = null;

        var report = await RunRepairAsync(lineage, dryRun: true);

        // The operator's first move: look before you write.
        Assert.True(report.DryRun);
        Assert.Equal(1, report.Missing);
        Assert.Equal(0, report.Repaired);
        Assert.Empty(lineage.Tags.Items);
        Assert.Equal(0, lineage.Tags.Creates);
    }
}
