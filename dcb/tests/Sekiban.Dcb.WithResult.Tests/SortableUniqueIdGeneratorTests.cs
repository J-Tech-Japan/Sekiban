using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Tests;

public class SortableUniqueIdGeneratorTests
{
    [Fact]
    public void FrozenClockConcurrentAllocationsHaveUniqueConsecutiveLogicalTicks()
    {
        var now = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
        var logger = new CountingLogger();
        var generator = new MonotonicSortableUniqueIdGenerator(new MutableTimeProvider(now), logger);
        var ids = new string[2_000];

        Parallel.For(0, ids.Length, index => ids[index] = generator.GenerateNew());

        var ticks = ids.Select(ReadTicks).Order().ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(now.UtcTicks, ticks[0]);
        Assert.Equal(now.UtcTicks + ids.Length - 1, ticks[^1]);
        Assert.All(ticks.Zip(ticks.Skip(1)), pair => Assert.Equal(pair.First + 1, pair.Second));
        Assert.All(ids, id => Assert.Equal(30, id.Length));
        Assert.Equal(0, logger.WarningCount);
    }

    [Fact]
    public void ClockRollbackAdvancesLogicalTicksAndLogsOnlyPhysicalRegression()
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero));
        var logger = new CountingLogger();
        var generator = new MonotonicSortableUniqueIdGenerator(time, logger);
        var first = generator.GenerateNew();

        time.UtcNow = time.UtcNow.AddMinutes(-5);
        var second = generator.GenerateNew();
        var third = generator.GenerateNew();

        Assert.True(string.CompareOrdinal(second, first) > 0);
        Assert.True(string.CompareOrdinal(third, second) > 0);
        Assert.Equal(1, logger.WarningCount);

        time.AdvanceTimestamp(TimeSpan.FromMinutes(1));
        generator.GenerateNew();
        Assert.Equal(2, logger.WarningCount);
    }

    [Fact]
    public void ConcurrentRollbackAndJitterRemainStrictlyMonotonicThroughProductionGenerator()
    {
        var anchor = new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time = new RollbackJitterTimeProvider(anchor);
        var generator = new MonotonicSortableUniqueIdGenerator(time);
        var highWater = generator.GenerateNew();
        time.BeginRollbackWithJitter();
        var ids = new string[4_000];

        Parallel.For(0, ids.Length, index => ids[index] = generator.GenerateNew());

        var ticks = ids.Select(ReadTicks).Order().ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ReadTicks(highWater) + 1, ticks[0]);
        Assert.Equal(ReadTicks(highWater) + ids.Length, ticks[^1]);
        Assert.All(ticks.Zip(ticks.Skip(1)), pair => Assert.Equal(pair.First + 1, pair.Second));
    }

    [Fact]
    public void GenerateAndGetIdStringHaveStableThirtyByteGolden()
    {
        var timestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("00000000000", SortableUniqueId.GetIdString(Guid.Empty));
        var generated = SortableUniqueId.Generate(timestamp, Guid.Empty);
        Assert.Equal("063082281600000000000000000000", generated);
        Assert.Equal(30, System.Text.Encoding.UTF8.GetByteCount(generated));
    }

    [Fact]
    public void EveryGenerationUsesAFreshGuidSuffix()
    {
        var generator = new MonotonicSortableUniqueIdGenerator(
            new MutableTimeProvider(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var suffixes = Enumerable.Range(0, 32)
            .Select(_ => generator.GenerateNew()[SortableUniqueId.TickNumberOfLength..])
            .ToArray();

        Assert.Equal(suffixes.Length, suffixes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(suffixes, suffix => Assert.Equal(SortableUniqueId.IdNumberOfLength, suffix.Length));
    }

    [Fact]
    public void SeedRaisesFloorAndMaxValueIsTypedExhaustion()
    {
        var generator = new MonotonicSortableUniqueIdGenerator(
            new MutableTimeProvider(DateTimeOffset.UnixEpoch));
        var floor = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks;

        generator.Seed(floor);
        Assert.Equal(floor + 1, ReadTicks(generator.GenerateNew()));

        generator.Seed(DateTime.MaxValue.Ticks);
        var error = Assert.Throws<InvalidOperationException>(() => generator.GenerateNew());
        Assert.Contains(nameof(DateTime.MaxValue), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DependencyInjectionUsesOneGeneratorAndCoordinator()
    {
        var services = new ServiceCollection();
        services.AddSekibanDcbSortableUniqueIdGenerator();
        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<ISortableUniqueIdGenerator>(),
            provider.GetRequiredService<ISortableUniqueIdGenerator>());
        Assert.Same(
            provider.GetRequiredService<SortableUniqueIdSeedCoordinator>(),
            provider.GetRequiredService<SortableUniqueIdSeedCoordinator>());
    }

    [Fact]
    public async Task ServiceSeedIsSingleFlightAndFailureCanRetry()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        var generator = new MonotonicSortableUniqueIdGenerator(time);
        var coordinator = new SortableUniqueIdSeedCoordinator(generator);
        var floor = new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero).UtcTicks;
        var head = SortableUniqueId.Generate(new DateTime(floor, DateTimeKind.Utc), Guid.NewGuid());
        var store = new HeadOnlyEventStore(head, failFirst: true);

        await Assert.ThrowsAsync<SortableUniqueIdSeedException>(
            () => coordinator.EnsureSeededAsync("service-a", store));

        await Task.WhenAll(
            Enumerable.Range(0, 32).Select(_ => coordinator.EnsureSeededAsync("service-a", store)));

        Assert.Equal(2, store.HeadReads);
        Assert.Equal(floor + 1, ReadTicks(generator.GenerateNew()));
    }

    [Fact]
    public async Task EmptyStoreSeedIsNoOp()
    {
        var generator = new CountingGenerator();
        var coordinator = new SortableUniqueIdSeedCoordinator(generator);

        await coordinator.EnsureSeededAsync("service-a", new HeadOnlyEventStore(string.Empty));

        Assert.Equal(0, generator.GenerateCalls);
        Assert.Equal(0, generator.SeedCalls);
    }

    private static long ReadTicks(string id) => long.Parse(
        id.AsSpan(0, SortableUniqueId.TickNumberOfLength),
        System.Globalization.CultureInfo.InvariantCulture);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private long _timestamp;
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public override long GetTimestamp() => Volatile.Read(ref _timestamp);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void AdvanceTimestamp(TimeSpan elapsed) => Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }

    private sealed class RollbackJitterTimeProvider(DateTimeOffset anchor) : TimeProvider
    {
        private int _jitterIndex;
        private int _rolledBack;

        public void BeginRollbackWithJitter() => Volatile.Write(ref _rolledBack, 1);

        public override DateTimeOffset GetUtcNow()
        {
            if (Volatile.Read(ref _rolledBack) == 0)
            {
                return anchor;
            }

            var jitter = 1 + Math.Abs(Interlocked.Increment(ref _jitterIndex) % 997);
            return anchor.AddTicks(-jitter);
        }
    }

    private sealed class CountingLogger : ILogger<MonotonicSortableUniqueIdGenerator>
    {
        private int _warningCount;
        public int WarningCount => Volatile.Read(ref _warningCount);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Interlocked.Increment(ref _warningCount);
            }
        }
    }

    private sealed class CountingGenerator : ISortableUniqueIdGenerator
    {
        public int GenerateCalls { get; private set; }
        public int SeedCalls { get; private set; }
        public string GenerateNew()
        {
            GenerateCalls++;
            return SortableUniqueId.Generate(DateTime.UtcNow, Guid.NewGuid());
        }
        public void Seed(long ticks) => SeedCalls++;
    }

    private sealed class HeadOnlyEventStore(string head, bool failFirst = false) : IEventStore
    {
        private int _headReads;
        public int HeadReads => Volatile.Read(ref _headReads);

        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync()
        {
            var read = Interlocked.Increment(ref _headReads);
            return Task.FromResult(failFirst && read == 1
                ? ResultBox.Error<string>(new InvalidOperationException("head unavailable"))
                : ResultBox.FromValue(head));
        }

        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => Throw<IEnumerable<TagStream>>();
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => Throw<TagState>();
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => Throw<bool>();
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => Throw<long>();
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => Throw<IEnumerable<TagInfo>>();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            Throw<IEnumerable<SerializableEvent>>();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount) => Throw<IEnumerable<SerializableEvent>>();
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) => Throw<SerializableEvent>();
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null) => Throw<IEnumerable<SerializableEvent>>();
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events) =>
            Throw<(IReadOnlyList<SerializableEvent>, IReadOnlyList<TagWriteResult>)>();

        private static Task<ResultBox<T>> Throw<T>() where T : notnull =>
            throw new InvalidOperationException("Unexpected event-store operation.");
    }
}
