using Dcb.Domain.Enrollment;
using Dcb.Domain.Student;
using Npgsql;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.TagConsistencyFence;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;
using Xunit.Abstractions;

namespace Sekiban.Dcb.Postgres.Tests;

public sealed class ObservedPositionPostgresClockSkewTests(PostgresTestFixture fixture, ITestOutputHelper output)
    : PostgresTestBase(fixture)
{
    private sealed record Command : ICommand;
    private sealed class MutableTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public static IEnumerable<object[]> SkewCases()
    {
        foreach (var mode in new[] { TagConsistencyFenceMode.Off, TagConsistencyFenceMode.DeriveFromReservations })
        foreach (var skew in new[] { 5000d, 5d })
        foreach (var thirdSlow in new[] { false, true })
            yield return [mode, skew, thirdSlow];
    }

    [Theory]
    [MemberData(nameof(SkewCases))]
    public async Task SlowClockWriter_SequentialCommands(TagConsistencyFenceMode mode, double skewMs, bool thirdSlow)
    {
        var service = new DefaultServiceIdProvider();
        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var epoch = new NpgsqlCommand(
                "INSERT INTO dcb_tag_head_enablement_epochs (\"ServiceId\", \"EnabledAtUtc\") VALUES (@service, @enabled)", connection);
            epoch.Parameters.AddWithValue("service", DefaultServiceIdProvider.DefaultServiceId);
            epoch.Parameters.AddWithValue("enabled", DateTime.UtcNow);
            await epoch.ExecuteNonQueryAsync();
        }
        var store = new PostgresEventStore(Fixture.DbContextFactory, Fixture.DomainTypes.EventTypes, service);
        var accessor = new InMemoryObjectAccessor(store, Fixture.DomainTypes);   // shared: no duplicate activation
        var t0 = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var fastClock = new MutableTime(t0.AddMilliseconds(skewMs));
        var slowClock = new MutableTime(t0);
        GeneralSekibanExecutor Executor(TimeProvider clock)
        {
            var generator = new MonotonicSortableUniqueIdGenerator(clock);
            return new GeneralSekibanExecutor(store, accessor, Fixture.DomainTypes, null, null, generator,
                new SortableUniqueIdSeedCoordinator(generator), service,
                tagConsistencyFenceOptions: new TagConsistencyFenceOptions { Mode = mode });
        }
        var fast = Executor(fastClock);
        var slow = Executor(slowClock);

        // Steady state: both processes have already written once (their once-per-service seed is consumed).
        foreach (var warm in new[] { slow, fast })
        {
            var wid = Guid.NewGuid();
            var w = await warm.ExecuteCommandAsync(ctx => ctx.AppendEvent(new StudentCreated(wid, "warmup"), new StudentTag(wid)));
            Assert.True(w.IsSuccess, w.IsSuccess ? "" : w.GetException().ToString());
        }

        var studentId = Guid.NewGuid();
        var tag = new StudentTag(studentId);
        async Task<(ResultBox<ExecutionResult> Result, int Seen)> EnrollAsync(GeneralSekibanExecutor executor, string label)
        {
            fastClock.Now += TimeSpan.FromMilliseconds(1);
            slowClock.Now += TimeSpan.FromMilliseconds(1);
            var seen = -1;
            var result = await executor.ExecuteAsync(new Command(), async (_, ctx) =>
            {
                var state = await ctx.GetStateAsync<StudentState, StudentProjector>(tag);
                if (!state.IsSuccess) return ResultBox.Error<EventOrNone>(state.GetException());
                seen = state.GetValue().Payload.EnrolledClassRoomIds.Count;
                if (state.GetValue().Payload.GetRemaining() <= 0)
                    return ResultBox.Error<EventOrNone>(new InvalidOperationException("student is full"));
                return await ctx.AppendEvent(new StudentEnrolledInClassRoom(studentId, Guid.NewGuid()), tag);
            });
            output.WriteLine($"[{mode}] {label}: success={result.IsSuccess} handlerSawEnrollments={seen} " +
                (result.IsSuccess ? $"id={result.GetValue().SortableUniqueId![..19]}" : $"error={result.GetException().GetType().Name}: {result.GetException().Message}"));
            return (result, seen);
        }

        fastClock.Now += TimeSpan.FromMilliseconds(1);
        slowClock.Now += TimeSpan.FromMilliseconds(1);
        var c1 = await fast.ExecuteCommandAsync(ctx => ctx.AppendEvent(new StudentCreated(studentId, "probe", 1), tag));
        Assert.True(c1.IsSuccess, c1.IsSuccess ? "" : c1.GetException().ToString());
        output.WriteLine($"[{mode}] cmd1 FAST create(max=1): id={c1.GetValue().SortableUniqueId![..19]}");
        var c2 = await EnrollAsync(slow, "cmd2 SLOW enroll");
        var c3 = await EnrollAsync(thirdSlow ? slow : fast, "cmd3 enroll");

        var rows = (await store.ReadSerializableEventsByTagAsync(tag)).GetValue().ToList();
        var enrollments = rows.Count(e => e.EventPayloadName == nameof(StudentEnrolledInClassRoom));
        output.WriteLine($"[{mode}] committed enrollments for a student with MaxClassCount=1: {enrollments}; " +
            $"ids in store order: {string.Join(" ", rows.Select(e => e.SortableUniqueIdValue[..19] + ":" + e.EventPayloadName))}");

        Assert.True(c2.Result.IsSuccess, c2.Result.IsSuccess ? "" : c2.Result.GetException().ToString());
        Assert.True(string.CompareOrdinal(c2.Result.GetValue().SortableUniqueId, c1.GetValue().SortableUniqueId) > 0);
        Assert.False(c3.Result.IsSuccess);
        Assert.Equal("student is full", c3.Result.GetException().Message);
        Assert.Equal(1, c3.Seen);
        Assert.Equal(1, enrollments);
    }
}
