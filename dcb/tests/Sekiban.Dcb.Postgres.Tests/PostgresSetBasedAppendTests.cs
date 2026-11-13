using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Postgres.DbModels;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Xunit;
using Xunit.Abstractions;

namespace Sekiban.Dcb.Postgres.Tests;

public sealed class PostgresSetBasedAppendTests(PostgresTestFixture fixture, ITestOutputHelper output)
    : PostgresTestBase(fixture)
{
    private const string Service = DefaultServiceIdProvider.DefaultServiceId;
    private PostgresEventStore Store() => new(Fixture.DbContextFactory, Fixture.DomainTypes.EventTypes, new FixedServiceIdProvider(Service));
    private static SerializableEvent Event(string position, string[] tags) => new(
        "{}"u8.ToArray(), position, Guid.CreateVersion7(), new EventMetadata("c", "r", "set-test"), tags.ToList(), "Marker");

    [Fact]
    public async Task UntaggedBatch_SendsNoHeadSql_AndKeepsAllSixHooks()
    {
        var store = Store();
        var hooks = 0;
        store.TagHeadProtocolHook = () => { hooks++; return Task.CompletedTask; };
        using var recorder = new CommandRecorder();
        using var parent = new Activity("untagged-count").Start();
        recorder.TraceId = parent.TraceId;
        var result = await store.WriteSerializableEventsAsync([Event("0010", [])]);
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        Assert.Empty(recorder.Commands);
        Assert.Equal(6, hooks);
        recorder.TraceId = default;
        await using var inspect = await Fixture.GetDbContextAsync();
        Assert.Equal(1, await inspect.Events.CountAsync());
    }

    [Fact]
    public async Task ManyTags_LazyExistingAndRepaired_PreserveHeadsAndAudit()
    {
        var tags = Enumerable.Range(0, 24).Select(i => $"many:{i:00}").ToArray();
        await using (var context = await Fixture.GetDbContextAsync())
        {
            foreach (var tag in tags.Take(16))
                context.TagHeads.Add(new DbTagHead { ServiceId = Service, Tag = tag, HeadPosition = "0010" });
            foreach (var tag in tags.Take(8).Concat(tags.Skip(20)))
                context.Tags.Add(DbTag.FromEventTag(tag, "many", "0020", Guid.CreateVersion7(), "Bypass", Service));
            await context.SaveChangesAsync();
        }
        var result = await Store().WriteSerializableEventsAsync([Event("0030", tags)]);
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        await using var inspect = await Fixture.GetDbContextAsync();
        Assert.Equal(24, await inspect.TagHeads.CountAsync(h => h.HeadPosition == "0030"));
        Assert.Equal(tags.Take(8), await inspect.TagHeadViolations.OrderBy(v => v.Tag).Select(v => v.Tag).ToArrayAsync());
    }

    [Fact]
    public async Task MissingKeys_DoNotBorrowPredecessorHeadsOrRepairs()
    {
        string[] tags = ["gap:a", "gap:c", "gap:e"];
        await using (var setup = await Fixture.GetDbContextAsync())
        {
            // The first requested key has only a predecessor in another service; the others have same-service
            // predecessors. Exercise both lazy bootstrap and reconciliation of an existing proven-empty head.
            setup.Tags.Add(DbTag.FromEventTag("gap:z", "gap", "0090", Guid.CreateVersion7(), "Bypass", "!prior-service"));
            foreach (var tag in new[] { "gap:b", "gap:d" })
                setup.Tags.Add(DbTag.FromEventTag(tag, "gap", "0090", Guid.CreateVersion7(), "Bypass", Service));
            setup.TagHeads.Add(new DbTagHead { ServiceId = Service, Tag = "gap:c" });
            await setup.SaveChangesAsync();
        }
        var result = await Store().WriteSerializableEventsAsync([Event("0030", tags)]);
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        await using var inspect = await Fixture.GetDbContextAsync();
        Assert.Equal(3, await inspect.TagHeads.CountAsync(h => h.ServiceId == Service && h.HeadPosition == "0030"));
        Assert.Empty(await inspect.TagHeadViolations.ToArrayAsync());
    }

    [Fact]
    public async Task StatementCounts_AreConstant_ForSameShapeSuccessAndRepairOnlyConflict()
    {
        // Count actual one-key repair conflicts separately from the historical repair+cleanup shape, whose
        // "1 tag" fixture actually has two distinct keys. Keep both shapes for main-growth evidence.
        var allCounts = new List<List<int>>();
        var sqlPaths = new List<bool>();
        foreach (var scenario in new[] { "success", "repair-only", "repair-cleanup" })
        {
            var repair = scenario != "success";
            var cleanup = scenario == "repair-cleanup";
            var counts = new List<int>();
            foreach (var size in new[] { 1, 2, 5, 20 })
            {
                var prefix = $"count:{scenario}:{size}:";
                var tags = Enumerable.Range(0, size).Select(i => prefix + i).ToArray();
                await using (var setup = await Fixture.GetDbContextAsync())
                {
                    foreach (var tag in tags)
                    {
                        setup.TagHeads.Add(new DbTagHead { ServiceId = Service, Tag = tag, HeadPosition = "0010" });
                        if (repair) setup.Tags.Add(DbTag.FromEventTag(tag, "count", "0020", Guid.CreateVersion7(), "Bypass", Service));
                    }
                    if (repair && !await setup.TagHeadEnablementEpochs.AnyAsync())
                        setup.TagHeadEnablementEpochs.Add(new DbTagHeadEnablementEpoch { ServiceId = Service, EnabledAtUtc = DateTime.UtcNow });
                    await setup.SaveChangesAsync();
                }
                using var recorder = new CommandRecorder();
                using var parent = new Activity("set-based-count").Start();
                recorder.TraceId = parent.TraceId;
                if (repair)
                {
                    var lazy = cleanup ? tags.Select(t => t + ":lazy").ToArray() : [];
                    var spec = new ExpectedTagPositionSpecification(tags.Select(t => new TagHeadExpectationEntry(Service, t, TagHeadExpectation.Exact("0010"))).ToArray());
                    var result = await Store().WriteSerializableEventsWithExpectedTagPositionsAsync([Event("0030", tags.Concat(lazy).ToArray())], spec);
                    Assert.False(result.IsSuccess);
                    Assert.IsType<ExpectedTagPositionConflictException>(result.GetException());
                    recorder.TraceId = default;
                    await using var inspect = await Fixture.GetDbContextAsync();
                    Assert.Equal(size, await inspect.TagHeadViolations.CountAsync(v => tags.Contains(v.Tag)));
                    Assert.False(await inspect.TagHeads.AnyAsync(h => lazy.Contains(h.Tag)));
                }
                else
                {
                    var result = await Store().WriteSerializableEventsAsync([Event("0030", tags)]);
                    Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
                }
                var count = recorder.Commands.Count;
                var useSetSql = size * (cleanup ? 2 : 1) >= 2;
                sqlPaths.Add(recorder.Commands.All(sql => sql.Contains("unnest(", StringComparison.OrdinalIgnoreCase) == useSetSql));
                Assert.True(count > 0, "Npgsql listener must observe raw seam commands.");
                output.WriteLine($"{scenario} {size} repaired/existing tags, {size * (cleanup ? 2 : 1)} distinct keys: {count}");
                counts.Add(count);
            }
            allCounts.Add(counts);
        }
        Assert.Equal(4, allCounts[0][0]); // main's one-key success
        Assert.Equal(5, allCounts[1][0]); // main's one-key repair-only conflict (no unrelated key)
        foreach (var counts in allCounts)
        {
            Assert.Equal(counts[1], counts[2]);
            Assert.Equal(counts[1], counts[3]);
        }
        Assert.Equal(new[] { 4, 4, 4, 4 }, allCounts[0]);
        Assert.Equal(new[] { 5, 5, 5, 5 }, allCounts[1]);
        Assert.Equal(new[] { 7, 7, 7, 7 }, allCounts[2]);
        Assert.All(sqlPaths, matches => Assert.True(matches, "Each phase must use SQL for the total distinct-key count."));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task SingleAndMultiKeyWriters_SharedKey_BothOrdersCompleteWithout40P01(bool singleFirst, bool lazy)
    {
        string[] many = ["mixed:z", "mixed:shared", "mixed:a"];
        if (!lazy)
        {
            await using var setup = await Fixture.GetDbContextAsync();
            setup.TagHeads.AddRange(many.Select(tag => new DbTagHead { ServiceId = Service, Tag = tag }));
            await setup.SaveChangesAsync();
        }
        var first = Store();
        var second = Store();
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        first.TagHeadProtocolHook = async () =>
        {
            if (++calls != 2) return;
            locked.SetResult(); // Phase 2 owns all of the first writer's keys.
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
        };
        var firstWrite = first.WriteSerializableEventsAsync([Event("0010", singleFirst ? ["mixed:shared"] : many)]);
        await locked.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var secondWrite = second.WriteSerializableEventsAsync([Event("0020", singleFirst ? many : ["mixed:shared"])]);
        try
        {
            await using var observer = new NpgsqlConnection(Fixture.ConnectionString);
            await observer.OpenAsync();
            var timer = Stopwatch.StartNew();
            while (true)
            {
                await using var waiting = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE '%dcb_tag_heads%'", observer);
                if ((long)(await waiting.ExecuteScalarAsync())! > 0) break;
                Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), "Second writer never waited on the shared key.");
                await Task.Delay(20);
            }
        }
        finally
        {
            release.TrySetResult();
            first.TagHeadProtocolHook = null;
        }
        var writes = await Task.WhenAll(firstWrite, secondWrite).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.All(writes, r => Assert.True(r.IsSuccess, r.IsSuccess ? "" : r.GetException().ToString()));
        await using var inspect = await Fixture.GetDbContextAsync();
        Assert.Equal("0020", await inspect.TagHeads.Where(h => h.Tag == "mixed:shared").Select(h => h.HeadPosition).SingleAsync());
        Assert.Equal(2, await inspect.Events.CountAsync());
    }

    [Fact]
    public async Task ReversedUnicodeBatches_DoNotDeadlock()
    {
        string[] tags = ["order:z", "order:Z", "order:é", "order:\U00010000", "order:\uE000"];
        Assert.True(StringComparer.Ordinal.Compare(tags[2], tags[0]) > 0);
        Assert.True(System.Globalization.CultureInfo.InvariantCulture.CompareInfo.Compare(tags[2], tags[0]) < 0);
        Assert.True(StringComparer.Ordinal.Compare(tags[3], tags[4]) < 0);
        Assert.True(System.Text.Encoding.UTF8.GetBytes(tags[3]).AsSpan().SequenceCompareTo(System.Text.Encoding.UTF8.GetBytes(tags[4])) > 0);
        var writes = await Task.WhenAll(
            Store().WriteSerializableEventsAsync([Event("0010", tags)]),
            Store().WriteSerializableEventsAsync([Event("0020", tags.Reverse().ToArray())])).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.All(writes, r => Assert.True(r.IsSuccess, r.IsSuccess ? "" : r.GetException().ToString()));
        await using var inspect = await Fixture.GetDbContextAsync();
        Assert.Equal(tags.Length, await inspect.TagHeads.CountAsync(h => h.HeadPosition == "0020"));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public async Task OrdinalOrder_DirectBlockingProbe(bool lazyInsert, int blockedIndex)
    {
        string[] keys = ["probe:\U00010000", "probe:\uE000"];
        if (!lazyInsert)
        {
            await using var setup = await Fixture.GetDbContextAsync();
            setup.TagHeads.AddRange(keys.Select(t => new DbTagHead { ServiceId = Service, Tag = t }));
            await setup.SaveChangesAsync();
        }
        await using var admin = new NpgsqlConnection(Fixture.ConnectionString);
        await admin.OpenAsync();
        await using var hold = await admin.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand(lazyInsert
            ? "INSERT INTO dcb_tag_heads (\"ServiceId\", \"Tag\") VALUES (@s, @t)"
            : "SELECT * FROM dcb_tag_heads WHERE \"ServiceId\" = @s AND \"Tag\" = @t FOR UPDATE", admin, hold))
        {
            command.Parameters.AddWithValue("s", Service);
            command.Parameters.AddWithValue("t", keys[blockedIndex]);
            await command.ExecuteNonQueryAsync();
        }
        var write = Store().WriteSerializableEventsAsync([Event("0010", keys.Reverse().ToArray())]);
        try
        {
            var timer = Stopwatch.StartNew();
            while (true)
            {
                await using var wait = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE '%dcb_tag_heads%'", admin, hold);
                if ((long)(await wait.ExecuteScalarAsync())! > 0) break;
                Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), "Seam never reached the held key.");
                await Task.Delay(20);
                // pg_stat_activity snapshots are transaction-local; clear before polling again.
                await using var clear = new NpgsqlCommand("SELECT pg_stat_clear_snapshot()", admin, hold);
                await clear.ExecuteNonQueryAsync();
            }
            await using var probe = new NpgsqlConnection(Fixture.ConnectionString);
            await probe.OpenAsync();
            await using var probeTx = await probe.BeginTransactionAsync();
            await using (var timeout = new NpgsqlCommand("SET LOCAL lock_timeout = '200ms'", probe, probeTx))
                await timeout.ExecuteNonQueryAsync();
            await using var check = new NpgsqlCommand(lazyInsert
                ? "INSERT INTO dcb_tag_heads (\"ServiceId\", \"Tag\") VALUES (@s, @t)"
                : "SELECT * FROM dcb_tag_heads WHERE \"ServiceId\" = @s AND \"Tag\" = @t FOR UPDATE NOWAIT", probe, probeTx);
            check.Parameters.AddWithValue("s", Service);
            check.Parameters.AddWithValue("t", keys[1 - blockedIndex]);
            if (blockedIndex == 1)
            {
                var error = await Assert.ThrowsAsync<PostgresException>(() => check.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.LockNotAvailable, error.SqlState);
            }
            else
                await check.ExecuteNonQueryAsync(); // Waiting for the ordinal-first key must not lock/insert the second.
        }
        finally
        {
            await hold.RollbackAsync();
            var result = await write.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        }
    }

    private sealed class CommandRecorder : IDisposable
    {
        private readonly ActivityListener _listener;
        public ActivityTraceId TraceId { get; set; }
        public ConcurrentQueue<string> Commands { get; } = new();
        public CommandRecorder()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Npgsql",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.TraceId != TraceId) return;
                    var sql = (activity.GetTagItem("db.query.text") ?? activity.GetTagItem("db.statement"))?.ToString() ?? activity.DisplayName;
                    if (sql.Contains("dcb_tag_heads", StringComparison.OrdinalIgnoreCase) ||
                        sql.Contains("dcb_tag_head_violations", StringComparison.OrdinalIgnoreCase) ||
                        (sql.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase) && sql.Contains("dcb_tags", StringComparison.OrdinalIgnoreCase)))
                        Commands.Enqueue(sql);
                }
            };
            ActivitySource.AddActivityListener(_listener);
        }
        public void Dispose() => _listener.Dispose();
    }
}
