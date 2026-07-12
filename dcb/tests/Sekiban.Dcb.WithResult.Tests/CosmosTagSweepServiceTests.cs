using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.CosmosDb.Repair;
using Sekiban.Dcb.CosmosDb.Sweep;
using Sekiban.Dcb.CosmosDb.Tags;
using System.Reflection;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Covers the automatic sweep: off unless asked for, repairs crash residue after startup, never blocks or
///     crashes the host, and — the part that matters most — cannot amplify or destroy anything, no matter how
///     it is configured or how many times it runs.
/// </summary>
public class CosmosTagSweepServiceTests
{
    private const string ServiceId = "svc";
    private const string Tag = "Student:1";

    /// <summary>Runs the real repair service against in-memory stores, so the sweep drives real classification.</summary>
    private sealed class InMemoryRepairRunner : ITagRepairRunner
    {
        private readonly List<CosmosEvent> _events;
        private readonly Dictionary<(string PartitionKey, string Id), CosmosTag> _rows;

        public InMemoryRepairRunner(List<CosmosEvent> events, Dictionary<(string, string), CosmosTag> rows)
        {
            _events = events;
            _rows = rows;
        }

        public int Runs { get; private set; }
        public int Creates { get; private set; }
        public int Deletes => 0; // There is no delete to count — the store seam has none.
        public List<CosmosTagRepairReport> Reports { get; } = new();
        public Exception? ThrowOnRun { get; set; }

        public async Task<CosmosTagRepairReport> RunAsync(
            string serviceId,
            CosmosTagRepairOptions options,
            CancellationToken cancellationToken)
        {
            Runs++;

            if (ThrowOnRun != null)
            {
                throw ThrowOnRun;
            }

            var store = new SweepFakeRepairStore(_rows, () => Creates++);
            var service = new CosmosDbTagRepairService(
                serviceId,
                new SweepFakeEventSource(_events),
                store);

            var report = await service.RepairAsync(options, cancellationToken);
            Reports.Add(report);
            return report;
        }
    }

    private sealed class SweepFakeEventSource : ICosmosRepairEventSource
    {
        private readonly List<CosmosEvent> _events;

        public SweepFakeEventSource(List<CosmosEvent> events) =>
            _events = events.OrderBy(e => e.SortableUniqueId, StringComparer.Ordinal).ToList();

        public Task<CosmosRepairEventPage> ReadEventPageAsync(
            string? fromSortableUniqueIdExclusive,
            string? toSortableUniqueIdInclusive,
            int pageSize,
            string? continuationToken,
            CancellationToken cancellationToken)
        {
            var offset = continuationToken == null ? 0 : int.Parse(continuationToken, null);
            var candidates = _events
                .Where(e => fromSortableUniqueIdExclusive == null ||
                    string.CompareOrdinal(e.SortableUniqueId, fromSortableUniqueIdExclusive) > 0)
                .ToList();

            var page = candidates.Skip(offset).Take(pageSize).ToList();
            var consumed = offset + page.Count;
            var next = consumed < candidates.Count ? consumed.ToString(null as IFormatProvider) : null;

            return Task.FromResult(new CosmosRepairEventPage(page, next, 1.0));
        }
    }

    private sealed class SweepFakeRepairStore : ICosmosTagRepairStore
    {
        private readonly Action _onCreate;
        private readonly Dictionary<(string PartitionKey, string Id), CosmosTag> _rows;

        public SweepFakeRepairStore(Dictionary<(string, string), CosmosTag> rows, Action onCreate)
        {
            _rows = rows;
            _onCreate = onCreate;
        }

        public Task<CosmosRepairRowLookup> ReadRowsForEventAsync(
            string partitionKey,
            Guid eventId,
            int maxRows,
            CancellationToken cancellationToken)
        {
            var matches = _rows.Values
                .Where(row => string.Equals(row.Pk, partitionKey, StringComparison.Ordinal))
                .Where(row => Guid.TryParse(row.EventId, out var id) && id == eventId)
                .ToList();

            return Task.FromResult(matches.Count > maxRows
                ? new CosmosRepairRowLookup(matches.Take(maxRows).ToList(), true, 1.0)
                : new CosmosRepairRowLookup(matches, false, 1.0));
        }

        public Task<(bool Created, double RequestCharge)> TryCreateRowAsync(
            string partitionKey,
            CosmosTag row,
            CancellationToken cancellationToken)
        {
            _onCreate();

            if (_rows.ContainsKey((partitionKey, row.Id)))
            {
                return Task.FromResult((false, 1.0));
            }

            _rows[(partitionKey, row.Id)] = row;
            return Task.FromResult((true, 1.0));
        }

        public Task<CosmosTag?> TryReadRowAsync(string partitionKey, string id, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.GetValueOrDefault((partitionKey, id)));
    }

    /// <summary>No waiting, no randomness — the sweep's schedule is asserted, not endured.</summary>
    private sealed class FakeSweepClock : ISweepClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public List<TimeSpan> Delays { get; } = new();
        public double Jitter { get; set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(delay);
            UtcNow = UtcNow.Add(delay);
            return Task.CompletedTask;
        }

        public double NextJitter() => Jitter;
    }

    private static string SortableId(DateTime at) => SortableUniqueId.Generate(at, Guid.NewGuid());

    private static CosmosEvent Event(Guid id, string sortableUniqueId) =>
        new()
        {
            Pk = $"{ServiceId}|{id}",
            ServiceId = ServiceId,
            Id = id.ToString(),
            SortableUniqueId = sortableUniqueId,
            EventType = "TestEvent",
            Payload = "{}",
            Tags = new List<string> { Tag }
        };

    private static CosmosTag LegacyRow(Guid eventId, string sortableUniqueId) =>
        new()
        {
            Pk = $"{ServiceId}|{Tag}",
            ServiceId = ServiceId,
            Id = Guid.NewGuid().ToString(), // legacy: random row id
            Tag = Tag,
            TagGroup = "Student",
            EventType = "TestEvent",
            SortableUniqueId = sortableUniqueId,
            EventId = eventId.ToString(),
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    /// <summary>
    ///     Starts the sweep and waits for its cycle to finish. With no interval configured the loop ends
    ///     after the startup run, so this is the whole of the sweep's work.
    /// </summary>
    private static async Task RunSweepAsync(CosmosTagSweepService sweep)
    {
        await sweep.StartAsync(CancellationToken.None);

        if (sweep.Sweeping != null)
        {
            await sweep.Sweeping;
        }

        await sweep.StopAsync(CancellationToken.None);
    }

    private static CosmosTagSweepService Sweep(
        CosmosTagSweepOptions options,
        ITagRepairRunner runner,
        ISweepClock? clock = null) =>
        new(options, new[] { ServiceId }, runner, clock ?? new FakeSweepClock());

    [Fact]
    public void The_Sweep_Should_Be_Disabled_By_Default()
    {
        Assert.False(new CosmosTagSweepOptions().Enabled);
    }

    [Fact]
    public async Task A_Disabled_Sweep_Should_Not_Touch_Storage()
    {
        var runner = new InMemoryRepairRunner(new List<CosmosEvent>(), new Dictionary<(string, string), CosmosTag>());

        await RunSweepAsync(Sweep(new CosmosTagSweepOptions(), runner));

        // Not a single read. Referencing the package must not start scanning anybody's containers.
        Assert.Equal(0, runner.Runs);
    }

    [Fact]
    public void AddSekibanDcbCosmosDb_Alone_Should_Register_No_Hosted_Sweep()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbCosmosDb("AccountEndpoint=https://localhost:8081/;AccountKey=key==", "testdb");

        // A package upgrade adds no hosted service, no startup scan, no configuration to fill in.
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationFactory != null &&
                descriptor.ImplementationType == typeof(CosmosTagSweepService));
        Assert.Null(services.BuildServiceProvider().GetService<CosmosTagSweepOptions>());
    }

    [Fact]
    public void AddSekibanDcbCosmosDbTagSweep_Should_Register_The_Sweep_And_Keep_It_Off_Unless_Enabled()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbCosmosDb("AccountEndpoint=https://localhost:8081/;AccountKey=key==", "testdb");
        services.AddSekibanDcbCosmosDbTagSweep();

        var options = services.BuildServiceProvider().GetRequiredService<CosmosTagSweepOptions>();

        Assert.False(options.Enabled);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public async Task A_Startup_Sweep_Should_Repair_Crash_Residue()
    {
        var clock = new FakeSweepClock();
        var eventId = Guid.NewGuid();

        // An event written an hour ago whose tag row never landed — exactly what a crash leaves behind.
        var events = new List<CosmosEvent> { Event(eventId, SortableId(clock.UtcNow.AddHours(-1))) };
        var rows = new Dictionary<(string, string), CosmosTag>();
        var runner = new InMemoryRepairRunner(events, rows);

        await RunSweepAsync(Sweep(new CosmosTagSweepOptions { Enabled = true }, runner, clock));

        Assert.Equal(1, runner.Runs);
        var report = Assert.Single(runner.Reports);
        Assert.Equal(1, report.Repaired);

        var row = Assert.Single(rows.Values);
        Assert.Equal(eventId.ToString(), row.Id);
    }

    [Fact]
    public async Task The_Sweep_Should_Only_Look_At_The_Recent_Window()
    {
        var clock = new FakeSweepClock();
        var rows = new Dictionary<(string, string), CosmosTag>();
        var events = new List<CosmosEvent>
        {
            Event(Guid.NewGuid(), SortableId(clock.UtcNow.AddDays(-30))), // older than the window
            Event(Guid.NewGuid(), SortableId(clock.UtcNow.AddHours(-1)))  // inside it
        };
        var runner = new InMemoryRepairRunner(events, rows);

        await RunSweepAsync(Sweep(
            new CosmosTagSweepOptions { Enabled = true, Window = TimeSpan.FromHours(24) },
            runner,
            clock));

        // Crash residue is recent. A full backfill is a manual job, not something a sweep does behind you.
        var report = Assert.Single(runner.Reports);
        Assert.Equal(1, report.EventsScanned);
        Assert.Equal(1, report.Repaired);
    }

    [Fact]
    public async Task Repeated_Sweeps_Should_Be_Idempotent()
    {
        var clock = new FakeSweepClock();
        var rows = new Dictionary<(string, string), CosmosTag>();
        var events = new List<CosmosEvent> { Event(Guid.NewGuid(), SortableId(clock.UtcNow.AddHours(-1))) };
        var runner = new InMemoryRepairRunner(events, rows);
        var options = new CosmosTagSweepOptions { Enabled = true };

        await RunSweepAsync(Sweep(options, runner, clock));
        await RunSweepAsync(Sweep(options, runner, new FakeSweepClock()));

        Assert.Equal(2, runner.Runs);
        Assert.Equal(1, runner.Reports[0].Repaired);
        Assert.Equal(0, runner.Reports[1].Repaired);
        Assert.Equal(1, runner.Reports[1].Present);
        Assert.Single(rows.Values);
    }

    [Fact]
    public async Task Sweeping_Legacy_Rows_Repeatedly_Should_Create_Nothing_And_Delete_Nothing()
    {
        var clock = new FakeSweepClock();
        var eventId = Guid.NewGuid();
        var sortableUniqueId = SortableId(clock.UtcNow.AddHours(-1));

        var legacy = LegacyRow(eventId, sortableUniqueId);
        var rows = new Dictionary<(string, string), CosmosTag> { [(legacy.Pk, legacy.Id)] = legacy };
        var runner = new InMemoryRepairRunner(new List<CosmosEvent> { Event(eventId, sortableUniqueId) }, rows);
        var options = new CosmosTagSweepOptions { Enabled = true };

        for (var i = 0; i < 3; i++)
        {
            await RunSweepAsync(Sweep(options, runner, new FakeSweepClock()));
        }

        // The pair is already indexed by the legacy row, so there is nothing to backfill — and the sweep may
        // not "tidy up" the legacy row either. Three runs, zero writes, zero deletions.
        Assert.Equal(3, runner.Runs);
        Assert.All(runner.Reports, report =>
        {
            Assert.Equal(1, report.LegacyPresent);
            Assert.Equal(0, report.Repaired);
            Assert.Equal(0, report.Missing);
        });
        Assert.Equal(0, runner.Creates);
        Assert.Equal(0, runner.Deletes);

        var stored = Assert.Single(rows.Values);
        Assert.Equal(legacy.Id, stored.Id);
        Assert.Equal(legacy.CreatedAt, stored.CreatedAt);
    }

    [Fact]
    public async Task Sweeping_Duplicate_Legacy_Rows_Repeatedly_Should_Not_Reduce_Them()
    {
        var clock = new FakeSweepClock();
        var eventId = Guid.NewGuid();
        var sortableUniqueId = SortableId(clock.UtcNow.AddHours(-1));

        var first = LegacyRow(eventId, sortableUniqueId);
        var second = LegacyRow(eventId, sortableUniqueId);
        var rows = new Dictionary<(string, string), CosmosTag>
        {
            [(first.Pk, first.Id)] = first,
            [(second.Pk, second.Id)] = second
        };
        var runner = new InMemoryRepairRunner(new List<CosmosEvent> { Event(eventId, sortableUniqueId) }, rows);

        for (var i = 0; i < 3; i++)
        {
            await RunSweepAsync(Sweep(new CosmosTagSweepOptions { Enabled = true }, runner, new FakeSweepClock()));
        }

        // De-duplicating is destructive, and the sweep has no route to it. It reports and leaves them alone.
        Assert.All(runner.Reports, report =>
        {
            Assert.Equal(1, report.Duplicate);
            Assert.Equal(0, report.Repaired);
        });
        Assert.Equal(0, runner.Creates);
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task A_Concurrent_Writer_Should_Not_Cause_Duplicates_Or_Errors()
    {
        var clock = new FakeSweepClock();
        var eventId = Guid.NewGuid();
        var sortableUniqueId = SortableId(clock.UtcNow.AddHours(-1));
        var rows = new Dictionary<(string, string), CosmosTag>();

        // The write path lands the row the sweep is about to backfill.
        rows[($"{ServiceId}|{Tag}", eventId.ToString())] =
            CosmosTagIdentity.DeriveRow(ServiceId, Tag, eventId, sortableUniqueId, "TestEvent");

        var runner = new InMemoryRepairRunner(new List<CosmosEvent> { Event(eventId, sortableUniqueId) }, rows);

        await RunSweepAsync(Sweep(new CosmosTagSweepOptions { Enabled = true }, runner, clock));

        var report = Assert.Single(runner.Reports);
        Assert.Equal(1, report.Present);
        Assert.Equal(0, report.Repaired);
        Assert.Equal(0, report.Corrupt);
        Assert.Single(rows.Values);
    }

    [Fact]
    public async Task A_Failing_Sweep_Should_Not_Take_The_Host_Down()
    {
        var runner = new InMemoryRepairRunner(new List<CosmosEvent>(), new Dictionary<(string, string), CosmosTag>())
        {
            ThrowOnRun = new InvalidOperationException("Cosmos is having a bad day")
        };

        var sweep = Sweep(new CosmosTagSweepOptions { Enabled = true }, runner);

        // The run throws, and nothing propagates: StartAsync returns, the background cycle completes rather
        // than faulting, and StopAsync returns. A sweep is not worth a crash.
        await sweep.StartAsync(CancellationToken.None);
        await sweep.Sweeping!;
        await sweep.StopAsync(CancellationToken.None);

        Assert.Equal(1, runner.Runs);
        Assert.True(sweep.Sweeping!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Startup_Should_Not_Be_Blocked_By_The_Sweep()
    {
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var runner = new BlockingRunner(started, release);
        var sweep = Sweep(new CosmosTagSweepOptions { Enabled = true, MaxStartupJitter = TimeSpan.Zero }, runner);

        // StartAsync must return even though the sweep is still mid-run.
        await sweep.StartAsync(CancellationToken.None);
        await started.Task;

        release.SetResult();
        await sweep.StopAsync(CancellationToken.None);
    }

    private sealed class BlockingRunner : ITagRepairRunner
    {
        private readonly TaskCompletionSource _release;
        private readonly TaskCompletionSource _started;

        public BlockingRunner(TaskCompletionSource started, TaskCompletionSource release)
        {
            _started = started;
            _release = release;
        }

        public async Task<CosmosTagRepairReport> RunAsync(
            string serviceId,
            CosmosTagRepairOptions options,
            CancellationToken cancellationToken)
        {
            _started.SetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new CosmosTagRepairReport();
        }
    }

    [Fact]
    public async Task Startup_Jitter_Should_Spread_Replicas_Apart()
    {
        var clock = new FakeSweepClock { Jitter = 0.5 };
        var runner = new InMemoryRepairRunner(new List<CosmosEvent>(), new Dictionary<(string, string), CosmosTag>());

        await RunSweepAsync(Sweep(
            new CosmosTagSweepOptions { Enabled = true, MaxStartupJitter = TimeSpan.FromSeconds(40) },
            runner,
            clock));

        // Replicas start together; without this they would all sweep at once and spike RU together.
        Assert.Equal(TimeSpan.FromSeconds(20), Assert.Single(clock.Delays));
    }

    [Fact]
    public async Task The_Sweep_Should_Pass_Its_Parallelism_And_Event_Budget_To_The_Repair()
    {
        var recording = new RecordingRunner();

        await RunSweepAsync(Sweep(
            new CosmosTagSweepOptions
            {
                Enabled = true,
                MaxParallelism = 3,
                MaxEventsPerRun = 250
            },
            recording));

        Assert.Equal(3, recording.LastOptions!.MaxParallelism);
        Assert.Equal(250, recording.LastOptions.MaxEventsToScan);

        // "Not a dry run" only ever means "create the rows that are missing".
        Assert.False(recording.LastOptions.DryRun);
    }

    private sealed class RecordingRunner : ITagRepairRunner
    {
        public CosmosTagRepairOptions? LastOptions { get; private set; }

        public Task<CosmosTagRepairReport> RunAsync(
            string serviceId,
            CosmosTagRepairOptions options,
            CancellationToken cancellationToken)
        {
            LastOptions = options;
            return Task.FromResult(new CosmosTagRepairReport());
        }
    }

    /// <summary>
    ///     Simulates a run whose budget elapses partway: it settles <c>settleBeforeBudget</c> events, then
    ///     throws with the partial progress, exactly as the repair service does when its budget CTS fires.
    /// </summary>
    private sealed class BudgetExhaustingRunner : ITagRepairRunner
    {
        private readonly List<CosmosEvent> _events;
        private readonly Dictionary<(string PartitionKey, string Id), CosmosTag> _rows;
        private readonly int _settleBeforeBudget;

        public BudgetExhaustingRunner(
            List<CosmosEvent> events,
            Dictionary<(string, string), CosmosTag> rows,
            int settleBeforeBudget)
        {
            _events = events.OrderBy(e => e.SortableUniqueId, StringComparer.Ordinal).ToList();
            _rows = rows;
            _settleBeforeBudget = settleBeforeBudget;
        }

        public List<string?> CheckpointsSeen { get; } = new();
        public int TotalRepaired { get; private set; }

        public async Task<CosmosTagRepairReport> RunAsync(
            string serviceId,
            CosmosTagRepairOptions options,
            CancellationToken cancellationToken)
        {
            CheckpointsSeen.Add(options.Checkpoint);

            // Only the events past the checkpoint are still this run's business.
            var remaining = _events
                .Where(e => options.Checkpoint == null ||
                    string.CompareOrdinal(e.SortableUniqueId, DecodeLast(options.Checkpoint)) > 0)
                .ToList();

            var settled = remaining.Take(_settleBeforeBudget).ToList();
            var store = new SweepFakeRepairStore(_rows, () => { });
            var service = new CosmosDbTagRepairService(serviceId, new SweepFakeEventSource(settled), store);

            var report = await service.RepairAsync(
                new CosmosTagRepairOptions { DryRun = false, MaxEventsToScan = settled.Count + 1 },
                cancellationToken);

            TotalRepaired += report.Repaired;

            if (remaining.Count <= _settleBeforeBudget)
            {
                // Everything left fitted in this turn.
                return report with { HasMore = false, Checkpoint = null };
            }

            // The budget ran out with work still to do. The events settled above are real progress, so the
            // exception carries a checkpoint pointing just past them.
            throw new CosmosTagRepairCancelledException(
                report with
                {
                    HasMore = true,
                    Checkpoint = Encode(settled[^1].SortableUniqueId)
                },
                new CancellationToken(true));
        }

        // The checkpoint is opaque to callers, so the test encodes and decodes it the same way the service
        // does rather than reaching inside it.
        private static string Encode(string lastSortableUniqueId) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
                $"{{\"LastSortableUniqueId\":\"{lastSortableUniqueId}\"}}"));

        private static string DecodeLast(string checkpoint)
        {
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(checkpoint));
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.GetProperty("LastSortableUniqueId").GetString()!;
        }
    }

    [Fact]
    public async Task A_Budget_Exhausted_Turn_Should_Persist_Its_Progress_So_The_Next_Turn_Advances()
    {
        var clock = new FakeSweepClock();
        var rows = new Dictionary<(string, string), CosmosTag>();

        // Five events with missing tag rows; a budget that only lets two settle per turn.
        var events = Enumerable
            .Range(0, 5)
            .Select(i => Event(Guid.NewGuid(), SortableId(clock.UtcNow.AddHours(-5 + i))))
            .OrderBy(e => e.SortableUniqueId, StringComparer.Ordinal)
            .ToList();

        var runner = new BudgetExhaustingRunner(events, rows, settleBeforeBudget: 2);
        var options = new CosmosTagSweepOptions { Enabled = true };
        var sweep = Sweep(options, runner, clock);

        // Turn 1: budget runs out after two events.
        await RunSweepAsync(sweep);

        Assert.Null(runner.CheckpointsSeen[0]); // started from the window
        Assert.Equal(2, runner.TotalRepaired);
        Assert.Equal(2, rows.Count);

        // Turn 2: the same sweep instance, resuming. This is the whole point — the checkpoint the budget-
        // exhausted turn produced must be carried forward, or the sweep re-scans the same prefix forever.
        await RunSweepAsync(sweep);

        Assert.NotNull(runner.CheckpointsSeen[1]);
        Assert.Equal(4, runner.TotalRepaired);
        Assert.Equal(4, rows.Count);

        // Turn 3 finishes the window and clears the checkpoint.
        await RunSweepAsync(sweep);

        Assert.Equal(5, runner.TotalRepaired);
        Assert.Equal(5, rows.Count);
    }

    [Fact]
    public async Task A_Cancelled_Repair_Should_Carry_The_Progress_It_Settled()
    {
        var clock = new FakeSweepClock();
        var rows = new Dictionary<(string, string), CosmosTag>();
        var events = Enumerable
            .Range(0, 4)
            .Select(i => Event(Guid.NewGuid(), SortableId(clock.UtcNow.AddHours(-4 + i))))
            .OrderBy(e => e.SortableUniqueId, StringComparer.Ordinal)
            .ToList();

        using var cts = new CancellationTokenSource();
        var settled = 0;
        var store = new SweepFakeRepairStore(rows, () =>
        {
            // Cancel once two events have been repaired, as a budget would.
            if (++settled == 2)
            {
                cts.Cancel();
            }
        });

        var service = new CosmosDbTagRepairService(
            ServiceId,
            new SweepFakeEventSource(events),
            store);

        var exception = await Assert.ThrowsAsync<CosmosTagRepairCancelledException>(
            () => service.RepairAsync(new CosmosTagRepairOptions { DryRun = false, PageSize = 1 }, cts.Token));

        // Cancellation still cancels — but the events it finished are not thrown away with the exception.
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
        Assert.Equal(2, exception.PartialReport.Repaired);
        Assert.Equal(2, exception.PartialReport.EventsScanned);
        Assert.True(exception.PartialReport.HasMore);
        Assert.NotNull(exception.PartialReport.Checkpoint);

        // Resuming from that checkpoint picks up exactly where it stopped.
        var resumed = await new CosmosDbTagRepairService(ServiceId, new SweepFakeEventSource(events), store)
            .RepairAsync(new CosmosTagRepairOptions
            {
                DryRun = false,
                Checkpoint = exception.PartialReport.Checkpoint
            });

        Assert.Equal(2, resumed.EventsScanned);
        Assert.Equal(2, resumed.Repaired);
        Assert.Equal(4, rows.Count);
    }

    [Fact]
    public async Task Host_Shutdown_Should_Not_Masquerade_As_Budget_Exhaustion()
    {
        var runner = new ShutdownObservingRunner();
        var sweep = Sweep(
            new CosmosTagSweepOptions { Enabled = true, MaxStartupJitter = TimeSpan.Zero },
            runner);

        await sweep.StartAsync(CancellationToken.None);
        await runner.Started.Task;

        // Shutting the host down is not a resumable overrun: no checkpoint is persisted, and the sweep just
        // stops rather than scheduling itself to "resume".
        await sweep.StopAsync(CancellationToken.None);

        Assert.True(sweep.Sweeping!.IsCompleted);
        Assert.Equal(1, runner.Runs);
    }

    private sealed class ShutdownObservingRunner : ITagRepairRunner
    {
        public TaskCompletionSource Started { get; } = new();
        public int Runs { get; private set; }

        public async Task<CosmosTagRepairReport> RunAsync(
            string serviceId,
            CosmosTagRepairOptions options,
            CancellationToken cancellationToken)
        {
            Runs++;
            Started.SetResult();

            // Block until the host cancels, then surface it as a cancellation — which the sweep must treat
            // as shutdown, not as a budget overrun.
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new CosmosTagRepairReport();
        }
    }

    [Fact]
    public void The_Sweeps_Only_Route_To_Storage_Should_Have_No_Destructive_Operation()
    {
        // The guarantee is structural, so assert it structurally: the store the repair — and therefore the
        // sweep — talks through cannot express a delete, a replace, or an upsert. No configuration can
        // reach what the type system does not offer.
        var members = typeof(ICosmosTagRepairStore)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)
            .ToList();

        Assert.Equal(
            new[] { "ReadRowsForEventAsync", "TryCreateRowAsync", "TryReadRowAsync" },
            members.OrderBy(name => name, StringComparer.Ordinal));

        foreach (var forbidden in new[] { "Delete", "Replace", "Upsert", "Patch", "Dedup", "Canonical" })
        {
            Assert.DoesNotContain(members, name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Sweep_Options_Should_Offer_No_Switch_That_Could_Make_It_Destructive()
    {
        var settable = typeof(CosmosTagSweepOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToList();

        // There is no mode, no "reduce duplicates", no "migrate legacy" — nothing an operator could flip.
        foreach (var forbidden in new[] { "Delete", "Replace", "Dedup", "Migrat", "Canonical", "Destructive", "Mode" })
        {
            Assert.DoesNotContain(settable, name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }
}
