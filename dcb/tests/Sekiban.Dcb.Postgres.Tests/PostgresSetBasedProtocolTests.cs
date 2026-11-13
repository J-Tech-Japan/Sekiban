using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Postgres.DbModels;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Xunit;

namespace Sekiban.Dcb.Postgres.Tests;

public sealed class PostgresSetBasedProtocolTests(PostgresTestFixture fixture) : PostgresTestBase(fixture)
{
    private const string Service = DefaultServiceIdProvider.DefaultServiceId;
    private static readonly string[] Keys = ["rev5:a", "rev5:z"];
    private PostgresEventStore Store(CountingLogger? logger = null) => new(
        Fixture.DbContextFactory, Fixture.DomainTypes.EventTypes, new FixedServiceIdProvider(Service), logger);
    private static SerializableEvent Event(string position, params string[] tags) => new(
        "{}"u8.ToArray(), position, Guid.CreateVersion7(), new EventMetadata("c", "r", "rev5"), tags.ToList(), "Marker");
    private static ExpectedTagPositionSpecification Spec(IEnumerable<string> tags, TagHeadExpectation expectation) =>
        new(tags.Select(tag => new TagHeadExpectationEntry(Service, tag, expectation)).ToArray());
    private async Task Seed(string? head = null, string? history = null, bool epoch = false)
    {
        await using var setup = await Fixture.GetDbContextAsync();
        if (head is not null) setup.TagHeads.AddRange(Keys.Select(tag => new DbTagHead { ServiceId = Service, Tag = tag, HeadPosition = head }));
        if (history is not null) setup.Tags.AddRange(Keys.Select(tag => DbTag.FromEventTag(tag, "rev5", history, Guid.CreateVersion7(), "OldWriter", Service)));
        if (epoch) setup.TagHeadEnablementEpochs.Add(new DbTagHeadEnablementEpoch { ServiceId = Service, EnabledAtUtc = DateTime.UtcNow });
        await setup.SaveChangesAsync();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task MultiKey_InjectedStageFailure_RollsBackLazyRepairAuditAndWrites(int stage)
    {
        await Seed("0100", "0200");
        var logger = new CountingLogger();
        var store = Store(logger);
        var calls = 0;
        store.TagHeadProtocolHook = () => ++calls == stage ? Task.FromException(new InvalidOperationException("injected")) : Task.CompletedTask;
        var result = await store.WriteSerializableEventsAsync([Event("0300", [.. Keys, "rev5:lazy"])]);
        Assert.False(result.IsSuccess);
        Assert.Contains("injected", result.GetException().ToString());
        // Independent physical connection, not the writer's tracked context or transaction.
        await using var fresh = new NpgsqlConnection(Fixture.ConnectionString);
        await fresh.OpenAsync();
        await using var query = new NpgsqlCommand("SELECT (SELECT count(*) FROM dcb_events), (SELECT count(*) FROM dcb_tags), (SELECT count(*) FROM dcb_tag_heads), (SELECT count(*) FROM dcb_tag_head_violations), (SELECT count(*) FROM dcb_tag_heads WHERE \"HeadPosition\" = '0100')", fresh);
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(new long[] { 0, 2, 2, 0, 2 }, Enumerable.Range(0, 5).Select(reader.GetInt64));
        Assert.Equal(0, logger.WarningCount);
    }

    [Fact]
    public async Task MultiKey_AuditRollbackThenRetry_IsIdempotentAndLogsOnlyCommittedRepairs()
    {
        await Seed("0100", "0200", true);
        var logger = new CountingLogger();
        var store = Store(logger);
        var calls = 0;
        store.TagHeadProtocolHook = () => ++calls == 3 ? Task.FromException(new InvalidOperationException("abort audit")) : Task.CompletedTask;
        string violationSql;
        using (var recorder = new Recorder())
        {
            var failed = await store.WriteSerializableEventsWithExpectedTagPositionsAsync([Event("0300", Keys)], Spec(Keys, TagHeadExpectation.Exact("0100")));
            Assert.False(failed.IsSuccess);
            violationSql = Assert.Single(recorder.Commands.Where(sql => sql.StartsWith("INSERT INTO dcb_tag_head_violations")));
        }
        await using (var fresh = await Fixture.GetDbContextAsync())
        {
            Assert.Empty(await fresh.TagHeadViolations.ToArrayAsync());
            Assert.Equal(2, await fresh.TagHeads.CountAsync(h => h.HeadPosition == "0100"));
            Assert.Empty(await fresh.Events.ToArrayAsync());
        }
        Assert.Equal(0, logger.WarningCount);
        store.TagHeadProtocolHook = null;
        for (var retry = 0; retry < 2; retry++)
        {
            using var recorder = new Recorder();
            var result = await store.WriteSerializableEventsWithExpectedTagPositionsAsync([Event("0300", Keys)], Spec(Keys, TagHeadExpectation.Exact("0100")));
            Assert.False(result.IsSuccess);
            Assert.IsType<ExpectedTagPositionConflictException>(result.GetException());
            Assert.Equal(retry == 0 ? 1 : 0, recorder.Commands.Count(sql => sql.StartsWith("INSERT INTO dcb_tag_head_violations")));
            await using var fresh = await Fixture.GetDbContextAsync();
            Assert.Equal(2, await fresh.TagHeadViolations.CountAsync());
            Assert.Equal(2, await fresh.TagHeads.CountAsync(h => h.HeadPosition == "0200"));
            Assert.Empty(await fresh.Events.ToArrayAsync());
            Assert.Equal(2, logger.WarningCount);
        }
        // Replay the exact set-based INSERT observed before rollback against the now-durable
        // audit identity. Ordinary stale append retries skip it; this also exercises DO NOTHING.
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var insert = new NpgsqlCommand(violationSql, connection, transaction);
        insert.Parameters.AddWithValue("serviceIds", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text, Keys.Select(_ => Service).ToArray());
        insert.Parameters.AddWithValue("tags", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text, Keys);
        insert.Parameters.AddWithValue("wasEmpty", new[] { false, false });
        insert.Parameters.AddWithValue("previous", new[] { "0100", "0100" });
        insert.Parameters.AddWithValue("observed", new[] { "0200", "0200" });
        insert.Parameters.AddWithValue("detectedAtUtc", DateTime.UtcNow);
        insert.Parameters.AddWithValue("writer", "ExpectedPositionBatch");
        Assert.Equal(0, await insert.ExecuteNonQueryAsync());
        Assert.Equal(0, await insert.ExecuteNonQueryAsync());
        await transaction.CommitAsync();
        await using var inspection = await Fixture.GetDbContextAsync();
        Assert.Equal(2, await inspection.TagHeadViolations.CountAsync());
        Assert.Equal(2, logger.WarningCount);
    }

    // The hook runs before any INSERT. Each writer waits for both entries, then the selected
    // first writer acquires its keys while the second is held until that acquisition is observed.
    private void Synchronize(PostgresEventStore first, PostgresEventStore second, bool abortFirst = false)
    {
        var entries = 0;
        var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCalls = 0;
        first.TagHeadProtocolHook = async () =>
        {
            var call = ++firstCalls;
            if (call == 1)
            {
                if (Interlocked.Increment(ref entries) == 2) both.TrySetResult();
                await both.Task.WaitAsync(TimeSpan.FromSeconds(20));
            }
            if (call == 2)
            {
                acquired.TrySetResult();
                // Keep the transaction open until the second writer has left its entry hook.
                await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(20));
                await using var observer = new NpgsqlConnection(Fixture.ConnectionString);
                await observer.OpenAsync();
                var timer = Stopwatch.StartNew();
                while (true)
                {
                    await using var waiting = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE '%dcb_tag_heads%'", observer);
                    if ((long)(await waiting.ExecuteScalarAsync())! > 0) break;
                    Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), "Second writer did not wait on the first creator.");
                    await Task.Delay(20);
                }
                if (abortFirst) throw new InvalidOperationException("rollback creator");
            }
        };
        var secondCalls = 0;
        second.TagHeadProtocolHook = async () =>
        {
            if (++secondCalls != 1) return;
            if (Interlocked.Increment(ref entries) == 2) both.TrySetResult();
            await both.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await acquired.Task.WaitAsync(TimeSpan.FromSeconds(20));
            secondEntered.TrySetResult();
        };
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MultiKey_AssertEmptyOrExactRace_CreatorCommitOrRollback_DetectsCreatedRows(bool exact, bool rollback)
    {
        // Exact also starts with absent heads: bootstrap must discover history only for rows
        // this transaction actually created, including after a blocked creator rolls back.
        await Seed(history: exact ? "0100" : null, epoch: true);
        var first = Store();
        var second = Store();
        Synchronize(first, second, rollback);
        var expectation = exact ? TagHeadExpectation.Exact("0100") : TagHeadExpectation.AssertEmpty();
        var results = await Task.WhenAll(
            first.WriteSerializableEventsWithExpectedTagPositionsAsync([Event("0200", Keys)], Spec(Keys, expectation)),
            second.WriteSerializableEventsWithExpectedTagPositionsAsync([Event("0300", Keys.Reverse().ToArray())], Spec(Keys, expectation)))
            .WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Single(results, result => result.IsSuccess);
        if (rollback) Assert.Contains("rollback creator", results[0].GetException().ToString());
        else Assert.IsType<ExpectedTagPositionConflictException>(results[1].GetException());
        await using var fresh = await Fixture.GetDbContextAsync();
        Assert.Single(await fresh.Events.ToArrayAsync());
        Assert.Equal(2, await fresh.TagHeads.CountAsync(h => h.HeadPosition == (rollback ? "0300" : "0200")));
        // A blocked non-creator must not bootstrap over a committed head and manufacture a repair.
        Assert.Empty(await fresh.TagHeadViolations.ToArrayAsync());
        Assert.Equal(exact ? 4 : 2, await fresh.Tags.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MultiKey_EnforcedLegacyRace_UsesMaximumOfReconciledHeadAndBatch(bool enforcedFirst)
    {
        await Seed("0100", "0500", true);
        var enforced = Store();
        var legacy = Store();
        Synchronize(enforcedFirst ? enforced : legacy, enforcedFirst ? legacy : enforced);
        var enforcedTask = enforced.WriteSerializableEventsWithExpectedTagPositionsAsync([Event("0600", Keys)], Spec(Keys, TagHeadExpectation.Exact("0500")));
        var legacyTask = legacy.WriteSerializableEventsAsync([Event("0300", Keys.Reverse().ToArray())]);
        await Task.WhenAll(enforcedTask, legacyTask).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True((await enforcedTask).IsSuccess, (await enforcedTask).IsSuccess ? "" : (await enforcedTask).GetException().ToString());
        Assert.True((await legacyTask).IsSuccess, (await legacyTask).IsSuccess ? "" : (await legacyTask).GetException().ToString());
        await using var fresh = await Fixture.GetDbContextAsync();
        Assert.Equal(2, await fresh.Events.CountAsync());
        Assert.Equal(2, await fresh.TagHeads.CountAsync(h => h.HeadPosition == "0600"));
        Assert.Equal(2, await fresh.TagHeadViolations.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MixedPaths_PhaseOneEntryBarrier_BothOrdersCommitWithoutDeadlock(bool singleFirst)
    {
        var first = Store();
        var second = Store();
        Synchronize(first, second);
        var results = await Task.WhenAll(
            first.WriteSerializableEventsAsync([Event("0300", singleFirst ? [Keys[0]] : Keys)]),
            second.WriteSerializableEventsAsync([Event("0200", singleFirst ? Keys : [Keys[0]])])).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.All(results, result => Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString()));
        await using var fresh = await Fixture.GetDbContextAsync();
        Assert.Equal("0300", await fresh.TagHeads.Where(h => h.Tag == Keys[0]).Select(h => h.HeadPosition).SingleAsync());
        Assert.Equal(2, await fresh.Events.CountAsync());
    }

    [Fact]
    public async Task ZeroDistinctKeys_RecorderSeesNoHeadStatement()
    {
        using var recorder = new Recorder();
        var result = await Store().WriteSerializableEventsAsync([Event("0100")]);
        Assert.True(result.IsSuccess);
        Assert.Empty(recorder.Commands);
    }

    [Theory]
    [InlineData("lazy")]
    [InlineData("bootstrap")]
    [InlineData("existing")]
    [InlineData("repair")]
    public async Task OneKey_RecordedTextsAndParameterNames_EqualMain(string shape)
    {
        if (shape is "existing" or "repair") await Seed("0100", shape == "repair" ? "0200" : null);
        if (shape == "bootstrap") await Seed(history: "0100");
        using var recorder = new Recorder();
        var result = await Store().WriteSerializableEventsAsync([Event("0300", Keys[0])]);
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.NotEmpty(recorder.Commands);
        // Captured by this identical test with ONLY PostgresEventStore.cs temporarily
        // swapped with origin/main e1478efe. Npgsql preserves the named placeholders.
        var statements = recorder.Commands.ToArray();
        var capture = Environment.GetEnvironmentVariable("G162_MAIN_SQL_CAPTURE");
        if (capture is not null)
            File.WriteAllText(Path.Combine(capture, shape + ".json"), JsonSerializer.Serialize(statements, new JsonSerializerOptions { WriteIndented = true }));
        else
        {
            var baseline = Path.Combine(AppContext.BaseDirectory, "MainSql", shape + ".json");
            var expected = JsonSerializer.Deserialize<string[]>(File.ReadAllText(baseline))!;
            Assert.Equal(expected, statements);
            static string[] Names(string sql) => Regex.Matches(sql, @"@(\w+)").Select(match => match.Groups[1].Value).Distinct().ToArray();
            Assert.Equal(expected.SelectMany(Names), statements.SelectMany(Names));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MultiKey_EmptySubsets_SkipBootstrapRepairCleanupAndAdvance(bool conflict)
    {
        await Seed("0300", epoch: true);
        using var recorder = new Recorder();
        // Existing heads and no newer history: an ordinary mismatch or a successful older
        // legacy batch with no head advances. Both send only insert/lock/reconcile.
        if (conflict)
        {
            var result = await Store().WriteSerializableEventsWithExpectedTagPositionsAsync([Event("0400", Keys)], Spec(Keys, TagHeadExpectation.Exact("0100")));
            Assert.False(result.IsSuccess);
            Assert.IsType<ExpectedTagPositionConflictException>(result.GetException());
        }
        else
        {
            var result = await Store().WriteSerializableEventsAsync([Event("0200", Keys)]);
            Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        }
        Assert.Equal(3, recorder.Commands.Count);
        Assert.DoesNotContain(recorder.Commands, sql => sql.StartsWith("UPDATE") || sql.StartsWith("DELETE") || sql.Contains("dcb_tag_head_violations"));
    }

    private sealed class Recorder : IDisposable
    {
        private readonly Activity _parent = new Activity("rev5-sql").Start();
        private readonly ActivityListener _listener;
        public ConcurrentQueue<string> Commands { get; } = new();
        public Recorder()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Npgsql",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.TraceId != _parent.TraceId) return;
                    var sql = (activity.GetTagItem("db.query.text") ?? activity.GetTagItem("db.statement"))?.ToString() ?? activity.DisplayName;
                    if (sql.Contains("dcb_tag_heads", StringComparison.OrdinalIgnoreCase) || sql.Contains("dcb_tag_head_violations", StringComparison.OrdinalIgnoreCase) ||
                        (sql.StartsWith("SELECT ") && sql.Contains("dcb_tags"))) Commands.Enqueue(sql);
                }
            };
            ActivitySource.AddActivityListener(_listener);
        }
        public void Dispose() { _listener.Dispose(); _parent.Dispose(); }
    }

    private sealed class CountingLogger : ILogger<PostgresEventStore>
    {
        public int WarningCount;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (level == LogLevel.Warning) Interlocked.Increment(ref WarningCount);
        }
    }
}
