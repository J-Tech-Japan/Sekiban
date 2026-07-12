using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.CosmosDb.Repair;
using Sekiban.Dcb.CosmosDb.Sweep;
using Sekiban.Dcb.CosmosDb.Tags;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Dcb.Domain;

namespace Sekiban.Dcb.Tests.Cosmos;

/// <summary>
///     The sweep, end to end, through the types that actually run in production: the registered
///     <see cref="CosmosTagSweepService" /> driving the real <see cref="CosmosTagRepairRunner" />, the real
///     <see cref="CosmosDbTagRepairService" />, and the in-memory Cosmos containers.
///     The sweep's own tests substitute a fake runner, which proves the scheduling but not that the wiring
///     reaches storage — the same "tested the fake, not the production path" gap that hid three real defects
///     in the earlier slices. These close it: nothing here is faked below the sweep.
///     Deterministic throughout. Turns are driven by starting the service, not by waiting for an interval;
///     cancellation is injected at an exact write, not polled for.
/// </summary>
public class CosmosSweepE2ETests
{
    private const string RuntimeServiceId = "runtime-svc";
    private const string ManagementServiceId = "management-svc";

    /// <summary>One service's containers, its repair factory, and the store that writes to them.</summary>
    private sealed class SweepLineage
    {
        public SweepLineage(string serviceId, InMemoryCosmosClient client, string eventsContainer, string tagsContainer)
        {
            ServiceId = serviceId;
            Client = client;

            Options = new CosmosDbEventStoreOptions
            {
                EventsContainerName = eventsContainer,
                TagsContainerName = tagsContainer,
                // A single attempt with no rollback reproduces what a crash leaves: durable events, no tags.
                WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward,
                TagWriteRetry = new CosmosTagWriteRetryOptions { MaxAttempts = 1, JitterRatio = 0 }
            };

            Context = new CosmosDbContext(client, "test-db", null, Options);
            Resolver = new DefaultCosmosContainerResolver(Options);
            Store = new CosmosDbEventStore(
                Context,
                DomainType.GetDomainTypes().EventTypes,
                new FixedServiceIdProvider(serviceId),
                Resolver);
            Factory = new CosmosDbTagRepairServiceFactory(Context, Resolver);
        }

        public string ServiceId { get; }
        public InMemoryCosmosClient Client { get; }
        public CosmosDbEventStoreOptions Options { get; }
        public CosmosDbContext Context { get; }
        public DefaultCosmosContainerResolver Resolver { get; }
        public CosmosDbEventStore Store { get; }
        public CosmosDbTagRepairServiceFactory Factory { get; }

        public InMemoryCosmosContainer Events => Client.Container(Options.EventsContainerName);
        public InMemoryCosmosContainer Tags => Client.Container(Options.TagsContainerName);

        /// <summary>Writes events whose tag rows never land — the residue a crash leaves behind.</summary>
        public async Task<List<SerializableEvent>> WriteCrashResidueAsync(int count)
        {
            Store.TagWriteFaultInjector = new AlwaysFailTagWrite();

            var written = new List<SerializableEvent>();
            for (var i = 0; i < count; i++)
            {
                var serializable = NewEvent($"Student:{ServiceId}:{i}");
                await Store.WriteSerializableEventsAsync(new[] { serializable });
                written.Add(serializable);
            }

            Store.TagWriteFaultInjector = null;
            return written;
        }
    }

    private sealed class FixedServiceIdProvider : IServiceIdProvider
    {
        private readonly string _serviceId;

        public FixedServiceIdProvider(string serviceId) => _serviceId = serviceId;

        public string GetCurrentServiceId() => _serviceId;
    }

    private sealed class AlwaysFailTagWrite : ICosmosTagWriteFaultInjector
    {
        public Task OnBeforeBatchAsync(int batchIndex, string partitionKey, IReadOnlyList<CosmosTag> rows) =>
            throw new InvalidOperationException("Injected crash before the tag write");
    }

    /// <summary>No waiting and no randomness: a sweep's schedule is asserted, never endured.</summary>
    private sealed class FakeSweepClock : ISweepClock
    {
        public DateTime UtcNow { get; set; } = DateTime.UtcNow;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UtcNow = UtcNow.Add(delay);
            return Task.CompletedTask;
        }

        public double NextJitter() => 0;
    }

    private static SerializableEvent NewEvent(string tag) =>
        new(
            System.Text.Encoding.UTF8.GetBytes("""{"Name":"test"}"""),
            SortableUniqueId.GenerateNew(),
            Guid.NewGuid(),
            new EventMetadata("causation", "correlation", "user"),
            new List<string> { tag },
            "TestEventPayload");

    /// <summary>Starts the sweep and waits for its cycle. With no interval, that is all of its work.</summary>
    private static async Task RunOneSweepTurnAsync(IHostedService sweep)
    {
        await sweep.StartAsync(CancellationToken.None);

        if (sweep is CosmosTagSweepService typed && typed.Sweeping != null)
        {
            await typed.Sweeping;
        }

        await sweep.StopAsync(CancellationToken.None);
    }

    // ── (1) The registered DI wiring reaches production repair ──────────────────────────────────────

    [Fact]
    public async Task An_Enabled_Sweep_Registered_Through_DI_Should_Repair_Missing_Tag_Rows()
    {
        var client = new InMemoryCosmosClient();
        var lineage = new SweepLineage(RuntimeServiceId, client, "events", "tags");
        await lineage.WriteCrashResidueAsync(2);

        Assert.Equal(2, lineage.Events.Items.Count);
        Assert.Empty(lineage.Tags.Items);

        // The registration a consumer actually writes — then the context is pointed at the in-memory account.
        var services = new ServiceCollection();
        services.AddSekibanDcbCosmosDb("AccountEndpoint=https://localhost:8081/;AccountKey=key==", "test-db");
        services.AddSekibanDcbCosmosDbTagSweep(sweep =>
        {
            sweep.Enabled = true;
            sweep.MaxStartupJitter = TimeSpan.Zero;
            sweep.Window = TimeSpan.FromDays(365);
        });

        services.Replace(ServiceDescriptor.Singleton(lineage.Options));
        services.Replace(ServiceDescriptor.Singleton(lineage.Context));
        services.Replace(ServiceDescriptor.Singleton<IServiceIdProvider>(
            new FixedServiceIdProvider(RuntimeServiceId)));

        var provider = services.BuildServiceProvider();

        // The hosted service the host would start — no test double anywhere below it.
        var sweep = provider.GetServices<IHostedService>().OfType<CosmosTagSweepService>().Single();

        await RunOneSweepTurnAsync(sweep);

        // The real runner reached the real repair, which wrote the rows the crash left missing.
        Assert.Equal(2, lineage.Tags.Items.Count);

        // And a tag-scoped read — the thing the whole chain exists for — now sees the event.
        var tag = $"Student:{RuntimeServiceId}:0";
        var byTag = await lineage.Store.ReadSerializableEventsByTagAsync(new SweepTag(tag));
        Assert.Single(byTag.GetValue());
    }

    private sealed record SweepTag(string Value) : Sekiban.Dcb.Tags.ITag
    {
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => Value.Split(':')[0];
        public string GetTag() => Value;
        public string GetTagContent() => Value.Split(':')[1];
    }

    // ── (2) A budget-exhausted turn carries its progress into the next one ──────────────────────────

    [Fact]
    public async Task A_Budget_Exhausted_Sweep_Turn_Should_Resume_Next_Turn_And_Clear_On_Completion()
    {
        var client = new InMemoryCosmosClient();
        var lineage = new SweepLineage(RuntimeServiceId, client, "events", "tags");
        await lineage.WriteCrashResidueAsync(4);

        // The real sweep, the real runner, the real repair — only the clock is substituted, so a turn costs
        // no wall-clock.
        var options = new CosmosTagSweepOptions
        {
            Enabled = true,
            MaxStartupJitter = TimeSpan.Zero,
            Window = TimeSpan.FromDays(365)
        };
        var sweep = new CosmosTagSweepService(
            options,
            new[] { RuntimeServiceId },
            new CosmosTagRepairRunner(lineage.Factory),
            new FakeSweepClock());

        // Turn 1: the run is cancelled once two rows are settled — exactly what the run budget elapsing does
        // to it, injected at a precise write rather than timed.
        lineage.Tags.OnWrite = alreadyWritten =>
        {
            if (alreadyWritten == 2)
            {
                throw new OperationCanceledException("Injected run-budget expiry");
            }
        };

        await RunOneSweepTurnAsync(sweep);

        lineage.Tags.OnWrite = null;
        Assert.Equal(2, lineage.Tags.Items.Count); // two settled before the budget went

        // Turn 2 resumes from the checkpoint the cancelled turn kept, and finishes the two it never reached.
        //
        // Asserting only the final row count would NOT prove that: a turn that dropped the checkpoint would
        // re-scan from the window, find the first two already present, repair the last two, and converge on
        // the same four rows. So count the work instead. The repair issues exactly one tag lookup per
        // (event, tag) key it examines, so a resumed turn examines two keys and a restarted one examines
        // four. Two is the proof; four is the bug.
        var lookupsBefore = lineage.Tags.Queries;

        await RunOneSweepTurnAsync(sweep);

        Assert.Equal(2, lineage.Tags.Queries - lookupsBefore);
        Assert.Equal(4, lineage.Tags.Items.Count);

        // Turn 3: completing the range cleared the checkpoint, so this turn starts from the window again —
        // all four keys examined, all four already indexed, nothing created and nothing rewritten. A turn
        // that had kept a stale checkpoint would examine none of them.
        var createsBefore = lineage.Tags.Creates;
        var lookupsBeforeTurn3 = lineage.Tags.Queries;

        await RunOneSweepTurnAsync(sweep);

        Assert.Equal(4, lineage.Tags.Queries - lookupsBeforeTurn3);
        Assert.Equal(4, lineage.Tags.Items.Count);
        Assert.Equal(createsBefore, lineage.Tags.Creates);
    }

    // ── (3) Lineages stay isolated through sweep execution ──────────────────────────────────────────

    [Fact]
    public async Task A_Sweep_Should_Not_Repair_Across_Lineages()
    {
        // Two lineages in one account — the management/runtime split a multi-tenant host has.
        var client = new InMemoryCosmosClient();
        var runtime = new SweepLineage(RuntimeServiceId, client, "runtime-events", "runtime-tags");
        var management = new SweepLineage(ManagementServiceId, client, "management-events", "management-tags");

        await runtime.WriteCrashResidueAsync(2);
        await management.WriteCrashResidueAsync(2);

        Assert.Empty(runtime.Tags.Items);
        Assert.Empty(management.Tags.Items);

        var options = new CosmosTagSweepOptions
        {
            Enabled = true,
            MaxStartupJitter = TimeSpan.Zero,
            Window = TimeSpan.FromDays(365)
        };

        // A sweep bound to the runtime lineage: its runner was built from the runtime factory, which resolved
        // the runtime containers at construction. There is no configuration that points it elsewhere.
        var runtimeSweep = new CosmosTagSweepService(
            options,
            new[] { RuntimeServiceId },
            new CosmosTagRepairRunner(runtime.Factory),
            new FakeSweepClock());

        await RunOneSweepTurnAsync(runtimeSweep);

        Assert.Equal(2, runtime.Tags.Items.Count);

        // The management lineage is untouched — not merely unrepaired, but never written to at all.
        Assert.Empty(management.Tags.Items);
        Assert.Equal(0, management.Tags.Creates);

        // Sweeping management repairs only management, and leaves runtime as it was.
        var managementSweep = new CosmosTagSweepService(
            options,
            new[] { ManagementServiceId },
            new CosmosTagRepairRunner(management.Factory),
            new FakeSweepClock());

        await RunOneSweepTurnAsync(managementSweep);

        Assert.Equal(2, management.Tags.Items.Count);
        Assert.Equal(2, runtime.Tags.Items.Count);

        // Every row sits in its own service's partition. Neither lineage's index mentions the other.
        Assert.All(
            runtime.Tags.Items,
            row => Assert.StartsWith(RuntimeServiceId, row["pk"]!.ToString(), StringComparison.Ordinal));
        Assert.All(
            management.Tags.Items,
            row => Assert.StartsWith(ManagementServiceId, row["pk"]!.ToString(), StringComparison.Ordinal));
    }

    // ── The disabled default, proven against the harness rather than a fake ─────────────────────────

    [Fact]
    public async Task A_Disabled_Sweep_Should_Not_Touch_The_Containers()
    {
        var client = new InMemoryCosmosClient();
        var lineage = new SweepLineage(RuntimeServiceId, client, "events", "tags");
        await lineage.WriteCrashResidueAsync(2);

        // Both containers, both dimensions. A sweep that is off must not scan events, must not look a single
        // tag key up, and must not write anywhere — so all four counters are pinned, not just the one that
        // happened to be convenient.
        var eventQueriesBefore = lineage.Events.Queries;
        var tagQueriesBefore = lineage.Tags.Queries;
        var eventCreatesBefore = lineage.Events.Creates;
        var tagCreatesBefore = lineage.Tags.Creates;

        // Registered but not enabled — the default.
        var sweep = new CosmosTagSweepService(
            new CosmosTagSweepOptions(),
            new[] { RuntimeServiceId },
            new CosmosTagRepairRunner(lineage.Factory),
            new FakeSweepClock());

        await RunOneSweepTurnAsync(sweep);

        Assert.Equal(eventQueriesBefore, lineage.Events.Queries);
        Assert.Equal(tagQueriesBefore, lineage.Tags.Queries);
        Assert.Equal(eventCreatesBefore, lineage.Events.Creates);
        Assert.Equal(tagCreatesBefore, lineage.Tags.Creates);
        Assert.Equal(0, lineage.Events.Deletes);
        Assert.Equal(0, lineage.Tags.Deletes);
        Assert.Empty(lineage.Tags.Items);
    }
}
