using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit.Abstractions;

namespace Sekiban.Dcb.Tests;

public partial class ObservedPositionClockSkewTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset T0 = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public enum StoreKind { InMemory, Sqlite }

    public static IEnumerable<object[]> SkewCases()
    {
        foreach (var store in Enum.GetValues<StoreKind>())
        foreach (var skew in new[] { 5000d, 5d })
        foreach (var writer in new[] { "FAST", "SLOW" })
        foreach (var serialized in new[] { false, true })
            yield return [store, skew, writer, serialized];
    }

    [Theory]
    [MemberData(nameof(SkewCases))]
    public async Task SlowClockWriter_PreservesObservedUpdates(StoreKind kind, double skewMs, string thirdWriter, bool serialized)
    {
        await using var rig = Rig.Create(kind, output);
        var fast = rig.NewExecutor("FAST", T0 + TimeSpan.FromMilliseconds(skewMs));
        var slow = rig.NewExecutor("SLOW", T0);
        var tag = new ProbeCounterTag("A-" + Guid.NewGuid().ToString("N"));

        // Both processes have been running: each has already written once for this service, so each one's
        // once-per-service store seed has ALREADY been consumed (this is the normal steady state of a deployment).
        await rig.WarmUpAsync(slow);
        await rig.WarmUpAsync(fast);

        rig.Advance(TimeSpan.FromMilliseconds(1));
        var c1 = await rig.IncrementAsync(fast, tag, "cmd1", serialized: serialized);
        rig.Advance(TimeSpan.FromMilliseconds(1));
        var c2 = await rig.IncrementAsync(slow, tag, "cmd2", serialized: serialized);
        rig.Advance(TimeSpan.FromMilliseconds(1));
        var c3 = await rig.IncrementAsync(thirdWriter == "FAST" ? fast : slow, tag, "cmd3", serialized: serialized);
        var report = await rig.ReportAsync(tag, c1, c2, c3);

        Assert.True(c1.Success && c2.Success && c3.Success);
        Assert.Equal(1, c2.SeenCount);
        Assert.True(string.CompareOrdinal(c2.EventId, c1.EventId) > 0);
        Assert.Equal(2, c3.SeenCount);
        Assert.Equal(3, report.StoredEvents);
        Assert.Equal(3, report.DistinctDecidedValues);
        Assert.Equal(report.FreshReplayCount, report.CachedActorCount);
    }

    [Fact]
    public async Task FirstWriteOfProcess_IsLiftedByStartupSeed()
    {
        await using var rig = Rig.Create(StoreKind.InMemory, output);
        var fast = rig.NewExecutor("FAST", T0 + TimeSpan.FromSeconds(5));
        var slow = rig.NewExecutor("SLOW", T0);
        var tag = new ProbeCounterTag("A2-" + Guid.NewGuid().ToString("N"));

        var c1 = await rig.IncrementAsync(fast, tag, "cmd1");
        var c2 = await rig.IncrementAsync(slow, tag, "cmd2");   // SLOW's very first write: seeds from the store head
        var c3 = await rig.IncrementAsync(fast, tag, "cmd3");
        await rig.ReportAsync(tag, c1, c2, c3);

        Assert.True(string.CompareOrdinal(c2.EventId, c1.EventId) > 0);
        Assert.Equal(2, c3.SeenCount);
    }

    [Fact]
    public async Task LaterIncrementalReads_KeepAllObservedEvents()
    {
        await using var rig = Rig.Create(StoreKind.InMemory, output);
        var fast = rig.NewExecutor("FAST", T0 + TimeSpan.FromSeconds(5));
        var slow = rig.NewExecutor("SLOW", T0);
        var tag = new ProbeCounterTag("A3-" + Guid.NewGuid().ToString("N"));
        await rig.WarmUpAsync(slow);
        await rig.WarmUpAsync(fast);

        var results = new List<CommandOutcome>
        {
            await rig.IncrementAsync(fast, tag, "cmd1"),
            await rig.IncrementAsync(slow, tag, "cmd2"),
            await rig.IncrementAsync(fast, tag, "cmd3"),
            await rig.IncrementAsync(fast, tag, "cmd4"),
            await rig.IncrementAsync(fast, tag, "cmd5")
        };
        var report = await rig.ReportAsync(tag, results.ToArray());
        output.WriteLine($"[A3] long-lived actor state count={report.CachedActorCount}, fresh full replay count={report.FreshReplayCount}, stored={report.StoredEvents}");
        Assert.Equal(5, report.StoredEvents);
        Assert.Equal(5, report.CachedActorCount);
        Assert.Equal(5, report.FreshReplayCount);
    }

    [Theory]
    [InlineData(StoreKind.InMemory)]
    [InlineData(StoreKind.Sqlite)]
    public async Task SingleGenerator_PreservesUpdates(StoreKind kind)
    {
        await using var rig = Rig.Create(kind, output);
        var only = rig.NewExecutor("ONLY", T0);
        var tag = new ProbeCounterTag("B-" + Guid.NewGuid().ToString("N"));
        await rig.WarmUpAsync(only);

        var c1 = await rig.IncrementAsync(only, tag, "cmd1");
        var c2 = await rig.IncrementAsync(only, tag, "cmd2");
        var c3 = await rig.IncrementAsync(only, tag, "cmd3");
        var report = await rig.ReportAsync(tag, c1, c2, c3);

        Assert.True(c1.Success && c2.Success && c3.Success);
        Assert.Equal(new[] { 0, 1, 2 }, new[] { c1.SeenCount, c2.SeenCount, c3.SeenCount });
        Assert.Equal(3, report.DistinctDecidedValues);
        Assert.Equal(3, report.CachedActorCount);
    }

    [Fact]
    public async Task TwoGeneratorsWithoutSkew_PreserveUpdates()
    {
        await using var rig = Rig.Create(StoreKind.InMemory, output);
        var a = rig.NewExecutor("P1", T0);
        var b = rig.NewExecutor("P2", T0);
        var tag = new ProbeCounterTag("B2-" + Guid.NewGuid().ToString("N"));
        await rig.WarmUpAsync(a);
        await rig.WarmUpAsync(b);
        rig.Advance(TimeSpan.FromMilliseconds(1));
        var c1 = await rig.IncrementAsync(a, tag, "cmd1");
        rig.Advance(TimeSpan.FromMilliseconds(1));
        var c2 = await rig.IncrementAsync(b, tag, "cmd2");
        rig.Advance(TimeSpan.FromMilliseconds(1));
        var c3 = await rig.IncrementAsync(a, tag, "cmd3");
        await rig.ReportAsync(tag, c1, c2, c3);
        Assert.Equal(new[] { 0, 1, 2 }, new[] { c1.SeenCount, c2.SeenCount, c3.SeenCount });
    }

    private sealed record CommandOutcome(string Label, string Writer, bool Success, string? Error, int SeenCount,
        string SeenLast, string EventId);

    private sealed record Report(int StoredEvents, int DistinctDecidedValues, int CachedActorCount, int FreshReplayCount);

    private sealed class NamedExecutor(string name, GeneralSekibanExecutor executor)
    {
        public string Name { get; } = name;
        public GeneralSekibanExecutor Executor { get; } = executor;
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly List<MutableTime> _clocks = [];
        private readonly string? _sqlitePath;
        public DcbDomainTypes Domain { get; }
        public IEventStore Store { get; }
        public InMemoryObjectAccessor Accessor { get; }
        public FixedService Service { get; } = new("default");

        private Rig(DcbDomainTypes domain, IEventStore store, string? sqlitePath, ITestOutputHelper output)
        {
            Domain = domain;
            Store = store;
            _sqlitePath = sqlitePath;
            _output = output;
            Accessor = new InMemoryObjectAccessor(store, domain);   // ONE accessor => one actor pair per tag
        }

        public static Rig Create(StoreKind kind, ITestOutputHelper output)
        {
            var domain = ProbeDomain.Build();
            if (kind == StoreKind.InMemory)
            {
                return new Rig(domain, new InMemoryConditionalEventStore(domain.EventTypes, new FixedService("default")), null, output);
            }
            var path = Path.Combine(Path.GetTempPath(), $"sek-g116-{Guid.NewGuid():N}.db");
            return new Rig(domain, new SqliteEventStore(path, domain.EventTypes, serviceIdProvider: new FixedService("default")),
                path, output);
        }

        public NamedExecutor NewExecutor(string name, DateTimeOffset now)
        {
            var clock = new MutableTime(now);
            _clocks.Add(clock);
            return NewExecutorWithProvider(name, clock);
        }

        public NamedExecutor NewExecutorWithProvider(string name, TimeProvider clock)
        {
            // Each "process" owns its generator AND its seed coordinator, exactly as ProcessSharedSortableUniqueIdServices does.
            var generator = new MonotonicSortableUniqueIdGenerator(clock);
            return new NamedExecutor(name, new GeneralSekibanExecutor(Store, Accessor, Domain, null, null, generator,
                new SortableUniqueIdSeedCoordinator(generator), Service));
        }

        public void Advance(TimeSpan by)
        {
            foreach (var clock in _clocks) clock.UtcNow += by;
        }

        public async Task WarmUpAsync(NamedExecutor executor)
        {
            var tag = new ProbeCounterTag("warmup-" + Guid.NewGuid().ToString("N"));
            var r = await executor.Executor.ExecuteAsync(new ProbeIncrement(),
                (_, _) => Task.FromResult(EventOrNone.Event(new ProbeCounterIncremented(0, "warmup"), tag)));
            Assert.True(r.IsSuccess, r.IsSuccess ? "" : r.GetException().ToString());
        }

        public async Task<CommandOutcome> IncrementAsync(NamedExecutor executor, ProbeCounterTag tag, string label, bool quiet = false, bool serialized = false)
        {
            var seenCount = -1;
            var seenLast = "?";
            if (serialized)
            {
                var actor = (await Accessor.GetActorAsync<ITagStateActorCommon>(
                    new TagStateId(tag, ProbeCounterProjector.ProjectorName).GetTagStateId())).GetValue();
                var state = await actor.GetStateAsync();
                seenCount = state.ResolvedPayloadName == nameof(ProbeCounterState)
                    ? ((ProbeCounterState)Domain.TagStatePayloadTypes.DeserializePayload(state.ResolvedPayloadName, state.Payload).GetValue()).Count
                    : 0;
                seenLast = state.LastSortedUniqueId;
                var commit = await ((ISerializedSekibanDcbExecutor)executor.Executor).CommitSerializableEventsAsync(
                    new SerializedCommitRequest(
                        [new SerializableEventCandidate(
                            JsonSerializer.SerializeToUtf8Bytes(new ProbeCounterIncremented(seenCount + 1, label), Domain.JsonSerializerOptions),
                            nameof(ProbeCounterIncremented), [tag.GetTag()])],
                        [new ConsistencyTagEntry(tag.GetTag(), seenLast)]));
                Assert.True(commit.IsSuccess, commit.IsSuccess ? "" : commit.GetException().ToString());
                var id = (await Store.ReadEventsByTagAsync(tag, Domain.EventTypes)).GetValue()
                    .Single(e => ((ProbeCounterIncremented)e.Payload).Label == label).SortableUniqueIdValue;
                return new CommandOutcome(label, executor.Name, true, null, seenCount, seenLast, id);
            }
            var result = await executor.Executor.ExecuteAsync(new ProbeIncrement(), async (_, context) =>
            {
                var state = await context.GetStateAsync<ProbeCounterProjector>(tag);
                if (!state.IsSuccess) return ResultBox.Error<EventOrNone>(state.GetException());
                var tagState = state.GetValue();
                seenCount = tagState.Payload is ProbeCounterState s ? s.Count : 0;
                seenLast = tagState.LastSortedUniqueId;
                // The decision: "the counter is N, so I make it N+1".
                return EventOrNone.Event(new ProbeCounterIncremented(seenCount + 1, label), tag);
            });
            var outcome = new CommandOutcome(label, executor.Name, result.IsSuccess,
                result.IsSuccess ? null : result.GetException().Message, seenCount, seenLast,
                result.IsSuccess ? result.GetValue().SortableUniqueId ?? "" : "");
            if (!quiet)
            {
                _output.WriteLine(
                    $"{label} by {executor.Name}: success={outcome.Success} handlerSawCount={seenCount} handlerSawLast={Short(seenLast)} " +
                    $"committedId={Short(outcome.EventId)} decidedNewValue={seenCount + 1}" +
                    (outcome.Success ? "" : $" error={outcome.Error}"));
            }
            return outcome;
        }

        public async Task<Report> ReportAsync(ProbeCounterTag tag, params CommandOutcome[] outcomes)
        {
            for (var i = 1; i < outcomes.Length; i++)
            {
                _output.WriteLine($"{outcomes[i].Label}.id < {outcomes[i - 1].Label}.id ? " +
                    (string.CompareOrdinal(outcomes[i].EventId, outcomes[i - 1].EventId) < 0));
            }

            var events = (await Store.ReadEventsByTagAsync(tag, Domain.EventTypes)).GetValue().ToList();
            _output.WriteLine($"store: {events.Count} events for tag, in store read order:");
            foreach (var e in events)
            {
                var p = (ProbeCounterIncremented)e.Payload;
                _output.WriteLine($"   id={Short(e.SortableUniqueIdValue)} by={p.Label} decidedNewValue={p.NewValue}");
            }
            var latest = (await Store.GetLatestTagAsync(tag)).GetValue().LastSortedUniqueId;
            _output.WriteLine($"store.GetLatestTagAsync = {Short(latest)}");

            // What the long-lived (shared, never re-activated) tag-state actor answers now.
            var stateId = new TagStateId(tag, ProbeCounterProjector.ProjectorName);
            var cached = await ReadVia(Accessor, stateId);
            // What a brand-new activation answers (fresh read of all events of the tag).
            var fresh = await ReadVia(new InMemoryObjectAccessor(Store, Domain), stateId);
            _output.WriteLine($"live tag-state actor: count={cached.Count} version={cached.Version} last={Short(cached.Last)}");
            _output.WriteLine($"fresh activation     : count={fresh.Count} version={fresh.Version} last={Short(fresh.Last)}");

            var decided = events.Select(e => ((ProbeCounterIncremented)e.Payload).NewValue).ToList();
            var distinct = decided.Distinct().Count();
            _output.WriteLine($"decided values = [{string.Join(",", decided)}] => " +
                (distinct == decided.Count ? "no lost update" : "LOST UPDATE (two commands decided the same value)"));
            return new Report(events.Count, distinct, cached.Count, fresh.Count);
        }

        private async Task<(int Count, int Version, string Last)> ReadVia(InMemoryObjectAccessor accessor, TagStateId id)
        {
            var actor = (await accessor.GetActorAsync<ITagStateActorCommon>(id.GetTagStateId())).GetValue();
            var state = await actor.GetStateAsync();
            var count = 0;
            if (state.ResolvedPayloadName == nameof(ProbeCounterState))
            {
                count = ((ProbeCounterState)Domain.TagStatePayloadTypes
                    .DeserializePayload(state.ResolvedPayloadName, state.Payload).GetValue()).Count;
            }
            return (count, state.Version, state.LastSortedUniqueId);
        }

        // 19 tick digits + first 4 of the random suffix is enough to read ordering.
        private static string Short(string? id) => string.IsNullOrEmpty(id) ? "(empty)" : id.Length >= 23 ? id[..19] + "~" + id[19..23] : id;

        public ValueTask DisposeAsync()
        {
            if (_sqlitePath is not null)
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                try { File.Delete(_sqlitePath); } catch { /* Best-effort temporary database cleanup. */ }
            }
            return ValueTask.CompletedTask;
        }
    }

    private static class ProbeDomain
    {
        public static DcbDomainTypes Build()
        {
            var eventTypes = new SimpleEventTypes();
            eventTypes.RegisterEventType<ProbeCounterIncremented>(nameof(ProbeCounterIncremented));
            var tagTypes = new SimpleTagTypes();
            tagTypes.RegisterTagGroupType<ProbeCounterTag>();
            var projectors = new SimpleTagProjectorTypes();
            projectors.RegisterProjector<ProbeCounterProjector>();
            var payloads = new SimpleTagStatePayloadTypes();
            payloads.RegisterPayloadType<ProbeCounterState>();
            return new DcbDomainTypes(eventTypes, tagTypes, projectors, payloads, new SimpleMultiProjectorTypes(),
                new SimpleQueryTypes(), new JsonSerializerOptions());
        }
    }

    public sealed record ProbeIncrement : ICommand;
    public sealed record ProbeCounterIncremented(int NewValue, string Label) : IEventPayload;
    public sealed record ProbeCounterState(int Count) : ITagStatePayload;

    public sealed record ProbeCounterTag(string Id) : IStringTagGroup<ProbeCounterTag>
    {
        public static string TagGroupName => "ProbeCounter";
        public static ProbeCounterTag FromContent(string content) => new(content);
        public bool IsConsistencyTag() => true;
        public string GetId() => Id;
    }

    public sealed class ProbeCounterProjector : ITagProjector<ProbeCounterProjector>
    {
        public static string ProjectorVersion => "1.0.0";
        public static string ProjectorName => nameof(ProbeCounterProjector);
        public static ITagStatePayload Project(ITagStatePayload current, Event ev) =>
            ev.Payload is ProbeCounterIncremented
                ? new ProbeCounterState((current as ProbeCounterState)?.Count + 1 ?? 1)
                : current;
    }

    private sealed class FixedService(string id) : IServiceIdProvider
    {
        public string GetCurrentServiceId() => id;
    }

    private sealed class MutableTime(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

}
