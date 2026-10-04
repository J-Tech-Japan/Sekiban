using System.Reflection;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Testing;

namespace Sekiban.Dcb.Tests;

public partial class ObservedPositionClockSkewTests
{
    private static string Position(long ticks) => ticks.ToString(SortableUniqueId.TickFormatter) + "00000000000";

    [Fact]
    public async Task Helper_ValidatesEveryCandidateBeforeMaximum_AndNeverLowersFloor()
    {
        await using var rig = Rig.Create(StoreKind.InMemory, output);
        var generator = new RecordingGenerator(T0);
        var core = new CoreGeneralSekibanExecutor(rig.Store, rig.Accessor, rig.Domain, null, null,
            generator, new SortableUniqueIdSeedCoordinator(generator), rig.Service);
        var seed = typeof(CoreGeneralSekibanExecutor).GetMethod("SeedAboveObservedPositions",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var ticks = T0.AddSeconds(5).Ticks;
        string?[] invalid = [null, "", "bad", new string('9', 30),
            Position(DateTime.MaxValue.Ticks + 1), new string('\u0669', 30), Position(ticks)[..29],
            "9223372036854775808" + "00000000000"];
        seed.Invoke(core, [invalid]);
        Assert.Empty(generator.Seeds);
        seed.Invoke(core, [invalid.Concat([Position(ticks - 1), Position(ticks), Position(ticks - 2)])]);
        Assert.Equal(new[] { ticks }, generator.Seeds);
        var first = generator.GenerateNew();
        seed.Invoke(core, [new[] { Position(ticks - 10), Position(0) }]);
        Assert.True(string.CompareOrdinal(generator.GenerateNew(), first) > 0);
        seed.Invoke(core, [new[] { Position(DateTime.MaxValue.Ticks) }]);
        Assert.Equal(DateTime.MaxValue.Ticks, generator.Seeds.Last());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeveralReservedTags_LiftEveryEventAboveLargest(bool serialized)
    {
        await using var rig = Rig.Create(StoreKind.InMemory, output);
        var generator = new RecordingGenerator(T0);
        var slow = await SeededExecutor(rig, generator);
        var fast = rig.NewExecutor("FAST", T0.AddSeconds(5));
        var a = new ProbeCounterTag("multi-a");
        var b = new ProbeCounterTag("multi-b");
        var first = await rig.IncrementAsync(fast, a, "a");
        rig.Advance(TimeSpan.FromMilliseconds(1));
        var largest = await rig.IncrementAsync(fast, b, "b");
        if (serialized)
        {
            var result = await ((ISerializedSekibanDcbExecutor)slow).CommitSerializableEventsAsync(
                new SerializedCommitRequest([Candidate(rig, a, "one"), Candidate(rig, b, "two")],
                    [new(a.GetTag(), first.EventId), new(b.GetTag(), largest.EventId)]));
            Assert.True(result.IsSuccess);
        }
        else
        {
            var result = await slow.ExecuteAsync(new ProbeIncrement(), async (_, ctx) =>
            {
                Assert.True((await ctx.GetStateAsync<ProbeCounterProjector>(a)).IsSuccess);
                Assert.True((await ctx.GetStateAsync<ProbeCounterProjector>(b)).IsSuccess);
                await ctx.AppendEvent(new ProbeCounterIncremented(2, "one"), a);
                return await ctx.AppendEvent(new ProbeCounterIncremented(2, "two"), b);
            });
            Assert.True(result.IsSuccess);
        }
        Assert.Equal(new[] { long.Parse(largest.EventId[..19]) }, generator.Seeds);
        var events = (await rig.Store.ReadAllSerializableEventsAsync()).GetValue()
            .Where(e => ((ProbeCounterIncremented)e.ToEvent(rig.Domain.EventTypes).GetValue().Payload).Label is "one" or "two");
        Assert.Equal(2, events.Count());
        Assert.All(events, e => Assert.True(string.CompareOrdinal(e.SortableUniqueIdValue, largest.EventId) > 0));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailedReservation_DoesNotSeedFromObservation(bool serialized, bool alreadySeeded)
    {
        await using var rig = Rig.Create(StoreKind.InMemory, output);
        var generator = new RecordingGenerator(T0);
        var coordinator = new SortableUniqueIdSeedCoordinator(generator);
        if (alreadySeeded) await coordinator.EnsureSeededAsync("default", rig.Store);
        var slow = Executor(rig, generator, coordinator);
        var tag = new ProbeCounterTag("failed-reservation");
        var fast = rig.NewExecutor("FAST", T0.AddSeconds(5));
        var head = await rig.IncrementAsync(fast, tag, "head");
        var fabricated = Position(T0.AddYears(1).Ticks);
        if (serialized)
        {
            var result = await ((ISerializedSekibanDcbExecutor)slow).CommitSerializableEventsAsync(
                new SerializedCommitRequest([Candidate(rig, tag, "blocked")], [new(tag.GetTag(), fabricated)]));
            Assert.False(result.IsSuccess);
        }
        else
        {
            var result = await slow.ExecuteAsync(new ProbeIncrement(), (_, _) => Task.FromResult(
                EventOrNone.Event(new ProbeCounterIncremented(2, "blocked"),
                    ConsistencyTag.FromTagWithSortableUniqueId(tag, new SortableUniqueId(fabricated)))));
            Assert.False(result.IsSuccess);
        }
        Assert.Equal(alreadySeeded ? Array.Empty<long>() : new[] { long.Parse(head.EventId[..19]) }, generator.Seeds);
        Assert.Equal(1, (await rig.Store.ReadEventsByTagAsync(tag, rig.Domain.EventTypes)).GetValue().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThrowingObservationSeed_CancelsReservations(bool serialized)
    {
        await using var rig = Rig.Create(StoreKind.InMemory, output);
        var generator = new RecordingGenerator(T0);
        var slow = await SeededExecutor(rig, generator);
        var tag = new ProbeCounterTag("seed-throw");
        var head = await rig.IncrementAsync(rig.NewExecutor("FAST", T0.AddSeconds(5)), tag, "head");
        generator.ThrowOnSeed = true;
        async Task<bool> Write()
        {
            if (serialized)
                return (await ((ISerializedSekibanDcbExecutor)slow).CommitSerializableEventsAsync(
                    new SerializedCommitRequest([Candidate(rig, tag, "retry")], [new(tag.GetTag(), head.EventId)]))).IsSuccess;
            return (await slow.ExecuteAsync(new ProbeIncrement(), async (_, ctx) =>
            {
                Assert.True((await ctx.GetStateAsync<ProbeCounterProjector>(tag)).IsSuccess);
                return EventOrNone.Event(new ProbeCounterIncremented(2, "retry"), tag);
            })).IsSuccess;
        }
        Assert.False(await Write());
        generator.ThrowOnSeed = false;
        Assert.True(await Write()); // Same actor and head: succeeds only if the first reservation was released.
        Assert.Equal(2, (await rig.Store.ReadEventsByTagAsync(tag, rig.Domain.EventTypes)).GetValue().Count());
    }

    [Theory]
    [InlineData("read-only")]
    [InlineData("non-consistency")]
    [InlineData("head-only")]
    [InlineData("unique-head-only")]
    [InlineData("unique-state")]
    public async Task LiftScope_UsesReservedTagsOrTrackedUniqueKeyStates(string path)
    {
        await using var rig = Rig.Create(StoreKind.InMemory, output);
        var generator = new RecordingGenerator(T0);
        var slow = await SeededExecutor(rig, generator);
        var a = new ProbeCounterTag("scope-a");
        var b = new ProbeCounterTag("scope-b");
        var head = await rig.IncrementAsync(rig.NewExecutor("FAST", T0.AddSeconds(5)), a, "head");
        var result = await slow.ExecuteAsync(new ProbeIncrement(), async (_, ctx) =>
        {
            if (path.Contains("head-only")) Assert.True((await ctx.GetTagLatestSortableUniqueIdAsync(a)).IsSuccess);
            else Assert.True((await ctx.GetStateAsync<ProbeCounterProjector>(a)).IsSuccess);
            ITag written = path == "non-consistency" ? new NonConsistencyTag(a) : b;
            return EventOrNone.Event(new ProbeCounterIncremented(2, "scope"), written);
        }, new CommandExecutionOptions
        {
            ConditionalAppend = path.StartsWith("unique") ? new ConditionalAppendSpecification("scope-key") : null
        });
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        var lifted = path == "unique-state";
        Assert.Equal(lifted, string.CompareOrdinal(result.GetValue().SortableUniqueId, head.EventId) > 0);
        Assert.Equal(lifted ? new[] { long.Parse(head.EventId[..19]) } : Array.Empty<long>(), generator.Seeds);
    }

    private static SerializableEventCandidate Candidate(Rig rig, ProbeCounterTag tag, string label) => new(
        JsonSerializer.SerializeToUtf8Bytes(new ProbeCounterIncremented(2, label), rig.Domain.JsonSerializerOptions),
        nameof(ProbeCounterIncremented), [tag.GetTag()]);

    private static GeneralSekibanExecutor Executor(Rig rig, RecordingGenerator generator,
        SortableUniqueIdSeedCoordinator coordinator) => new(rig.Store, rig.Accessor, rig.Domain,
        null, null, generator, coordinator, rig.Service);

    private static async Task<GeneralSekibanExecutor> SeededExecutor(Rig rig, RecordingGenerator generator)
    {
        var coordinator = new SortableUniqueIdSeedCoordinator(generator);
        await coordinator.EnsureSeededAsync("default", rig.Store);
        return Executor(rig, generator, coordinator);
    }

    private sealed class RecordingGenerator(DateTimeOffset now) : ISortableUniqueIdGenerator
    {
        private readonly MonotonicSortableUniqueIdGenerator _inner = new(new MutableTime(now));
        public List<long> Seeds { get; } = [];
        public bool ThrowOnSeed { get; set; }
        public string GenerateNew() => _inner.GenerateNew();
        public void Seed(long ticks)
        {
            Seeds.Add(ticks);
            if (ThrowOnSeed) throw new InvalidOperationException("seed failed");
            _inner.Seed(ticks);
        }
    }
}
