using System.Text.Json;
using Dcb.Domain;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;

namespace Sekiban.Dcb.Tests;

public class SortableUniqueIdProductionPathTests
{
    [Fact]
    public async Task AllFourCoreAllocationBranchesUseInjectedGenerator()
    {
        var domain = BuildDomain();
        var service = new FixedServiceIdProvider("service-a");
        var inner = new InMemoryConditionalEventStore(domain.EventTypes, service);
        var store = new CountingConditionalStore(inner);
        var accessor = new InMemoryObjectAccessor(store, domain);
        var generator = new RecordingGenerator(new MonotonicSortableUniqueIdGenerator(new FrozenTimeProvider()));
        var executor = new GeneralSekibanExecutor(
            store,
            accessor,
            domain,
            null,
            null,
            generator,
            new SortableUniqueIdSeedCoordinator(generator),
            service);

        var typedMulti = await executor.ExecuteAsync(
            new PathCommand(),
            async (_, context) =>
            {
                await context.AppendEvent(new PathEvent("typed-1"), new PathTag("typed-1"));
                return await context.AppendEvent(new PathEvent("typed-2"), new PathTag("typed-2"));
            });
        Assert.True(typedMulti.IsSuccess);

        var serialized = (ISerializedSekibanDcbExecutor)executor;
        var serializedMulti = await serialized.CommitSerializableEventsAsync(
            new SerializedCommitRequest(
                [Candidate(domain, "serialized-1"), Candidate(domain, "serialized-2")],
                []));
        Assert.True(serializedMulti.IsSuccess);

        var typedConditional = await executor.ExecuteAsync(
            new PathCommand(),
            (_, _) => Task.FromResult(
                EventOrNone.Event(new PathEvent("typed-conditional"), new PathTag("typed-conditional"))),
            new CommandExecutionOptions
            {
                ConditionalAppend = new ConditionalAppendSpecification("typed-key")
            });
        Assert.True(typedConditional.IsSuccess);

        var serializedConditional = await ((ISerializedConditionalSekibanDcbExecutor)executor)
            .CommitSerializableEventConditionallyAsync(
                new SerializedConditionalCommitRequest(
                    SerializedConditionalCommitRequest.CurrentVersion,
                    Candidate(domain, "serialized-conditional"),
                    "serialized-key"));
        Assert.True(serializedConditional.IsSuccess);

        Assert.Equal(6, generator.GenerateCalls);
        Assert.Equal(1, store.HeadReads);
        var written = (await store.ReadAllSerializableEventsAsync()).GetValue().ToArray();
        Assert.Equal(6, written.Length);
        Assert.Equal(generator.GeneratedIds.Order(StringComparer.Ordinal), written.Select(e => e.SortableUniqueIdValue));
    }

    [Fact]
    public async Task HeadFailureReturnsTypedErrorBeforeReservationAllocationOrWriteAndRetries()
    {
        var domain = BuildDomain();
        var service = new FixedServiceIdProvider("service-a");
        var inner = new InMemoryConditionalEventStore(domain.EventTypes, service);
        var store = new CountingConditionalStore(inner) { FailHeadReads = true };
        var actors = new CountingActorAccessor();
        var generator = new RecordingGenerator(new MonotonicSortableUniqueIdGenerator(new FrozenTimeProvider()));
        var executor = new GeneralSekibanExecutor(
            store,
            actors,
            domain,
            null,
            null,
            generator,
            new SortableUniqueIdSeedCoordinator(generator),
            service);

        var failed = await executor.ExecuteAsync(
            new PathCommand(),
            (_, _) => Task.FromResult(
                EventOrNone.Event(new PathEvent("blocked"), new ConsistencyPathTag("blocked"))));

        Assert.False(failed.IsSuccess);
        Assert.IsType<SortableUniqueIdSeedException>(failed.GetException());
        Assert.Equal(0, generator.GenerateCalls);
        Assert.Equal(0, actors.Calls);
        Assert.Equal(0, store.WriteCalls);

        store.FailHeadReads = false;
        var retried = await executor.ExecuteAsync(
            new PathCommand(),
            (_, _) => Task.FromResult(
                EventOrNone.Event(new PathEvent("retry"), new PathTag("retry"))));

        Assert.True(retried.IsSuccess);
        Assert.Equal(2, store.HeadReads);
        Assert.Equal(1, generator.GenerateCalls);
        Assert.Equal(1, store.WriteCalls);
    }

    [Fact]
    public async Task MalformedHeadFailsBeforeReservationAllocationOrWriteThenRereadsAndRetries()
    {
        var domain = BuildDomain();
        var service = new FixedServiceIdProvider("service-malformed");
        var inner = new InMemoryConditionalEventStore(domain.EventTypes, service);
        var store = new CountingConditionalStore(inner) { HeadOverride = "malformed" };
        var actors = new CountingActorAccessor();
        var generator = new RecordingGenerator(new MonotonicSortableUniqueIdGenerator(new FrozenTimeProvider()));
        var executor = new GeneralSekibanExecutor(
            store,
            actors,
            domain,
            null,
            null,
            generator,
            new SortableUniqueIdSeedCoordinator(generator),
            service);

        var failed = await executor.ExecuteAsync(
            new PathCommand(),
            (_, _) => Task.FromResult(
                EventOrNone.Event(new PathEvent("blocked"), new ConsistencyPathTag("blocked"))));

        Assert.False(failed.IsSuccess);
        Assert.IsType<SortableUniqueIdSeedException>(failed.GetException());
        Assert.Equal(1, store.HeadReads);
        Assert.Equal(0, generator.GenerateCalls);
        Assert.Equal(0, actors.ReservationCalls);
        Assert.Equal(0, store.WriteCalls);

        var validTicks = new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        store.HeadOverride = SortableUniqueId.Generate(
            new DateTime(validTicks, DateTimeKind.Utc),
            Guid.NewGuid());
        var retried = await executor.ExecuteAsync(
            new PathCommand(),
            (_, _) => Task.FromResult(
                EventOrNone.Event(new PathEvent("retry"), new ConsistencyPathTag("retry"))));

        Assert.True(retried.IsSuccess);
        Assert.Equal(2, store.HeadReads);
        Assert.Equal(1, generator.GenerateCalls);
        Assert.Equal(1, actors.ReservationCalls);
        Assert.Equal(1, store.WriteCalls);
        Assert.True(ReadTicks(generator.GeneratedIds.Single()) > validTicks);
    }

    [Fact]
    public async Task AmbientServiceChangeSeedsEachServiceOnceBeforeItsFirstProductionWrite()
    {
        var domain = BuildDomain();
        var service = new MutableServiceIdProvider("service-a");
        var inner = new InMemoryConditionalEventStore(domain.EventTypes, service);
        var headA = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        var headB = new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        await inner.WriteSerializableEventsAsync([Persisted(domain, "head-a", headA)]);
        service.Current = "service-b";
        await inner.WriteSerializableEventsAsync([Persisted(domain, "head-b", headB)]);

        var store = new CountingConditionalStore(inner, service);
        var accessor = new InMemoryObjectAccessor(store, domain);
        var generator = new RecordingGenerator(new MonotonicSortableUniqueIdGenerator(new FrozenTimeProvider()));
        var executor = new GeneralSekibanExecutor(
            store,
            accessor,
            domain,
            null,
            null,
            generator,
            new SortableUniqueIdSeedCoordinator(generator),
            service);

        service.Current = "service-a";
        Assert.True((await executor.ExecuteAsync(
            new PathCommand(),
            (_, _) => Task.FromResult(EventOrNone.Event(new PathEvent("a"), new PathTag("a"))))).IsSuccess);
        service.Current = "service-b";
        Assert.True((await executor.ExecuteAsync(
            new PathCommand(),
            (_, _) => Task.FromResult(EventOrNone.Event(new PathEvent("b"), new PathTag("b"))))).IsSuccess);
        service.Current = "service-a";
        Assert.True((await executor.ExecuteAsync(
            new PathCommand(),
            (_, _) => Task.FromResult(EventOrNone.Event(new PathEvent("a2"), new PathTag("a2"))))).IsSuccess);

        Assert.Equal(1, store.HeadReadsByService["service-a"]);
        Assert.Equal(1, store.HeadReadsByService["service-b"]);
        Assert.True(ReadTicks(generator.GeneratedIds[0]) > headA);
        Assert.True(ReadTicks(generator.GeneratedIds[1]) > headB);
        Assert.True(ReadTicks(generator.GeneratedIds[2]) > ReadTicks(generator.GeneratedIds[1]));
    }

    [Fact]
    public async Task RolledBackClockEventRemainsVisibleToExclusiveCheckpointCatchUp()
    {
        var domain = BuildDomain();
        var service = new FixedServiceIdProvider("service-a");
        var store = new InMemoryConditionalEventStore(domain.EventTypes, service);
        var accessor = new InMemoryObjectAccessor(store, domain);
        var time = new MutableTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var generator = new MonotonicSortableUniqueIdGenerator(time);
        var executor = new GeneralSekibanExecutor(
            store,
            accessor,
            domain,
            null,
            null,
            generator,
            new SortableUniqueIdSeedCoordinator(generator),
            service);

        var first = await executor.ExecuteAsync(
            new PathCommand(),
            (_, _) => Task.FromResult(EventOrNone.Event(new PathEvent("before"), new PathTag("before"))));
        Assert.True(first.IsSuccess);
        var checkpoint = first.GetValue().SortableUniqueId!;

        time.UtcNow = time.UtcNow.AddYears(-10);
        var afterRollback = await executor.ExecuteAsync(
            new PathCommand(),
            (_, _) => Task.FromResult(EventOrNone.Event(new PathEvent("after"), new PathTag("after"))));
        Assert.True(afterRollback.IsSuccess);

        var catchUp = (await store.ReadAllSerializableEventsAsync(new SortableUniqueId(checkpoint))).GetValue().ToArray();
        Assert.Single(catchUp);
        Assert.Equal(afterRollback.GetValue().SortableUniqueId, catchUp[0].SortableUniqueIdValue);
    }

    [Fact]
    public async Task SqliteRestartSeedsRealStoreHeadAndPreservesRollbackCatchUpCompleteness()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"sek-g31-{Guid.NewGuid():N}.db");
        try
        {
            var domain = BuildDomain();
            var service = new FixedServiceIdProvider("service-a");
            var firstStore = new SqliteEventStore(databasePath, domain.EventTypes, serviceIdProvider: service);
            var firstTime = new MutableTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var firstGenerator = new MonotonicSortableUniqueIdGenerator(firstTime);
            var firstExecutor = new GeneralSekibanExecutor(
                firstStore,
                new InMemoryObjectAccessor(firstStore, domain),
                domain,
                null,
                null,
                firstGenerator,
                new SortableUniqueIdSeedCoordinator(firstGenerator),
                service);
            var first = await firstExecutor.ExecuteAsync(
                new PathCommand(),
                (_, _) => Task.FromResult(EventOrNone.Event(new PathEvent("before"), new PathTag("before"))));
            Assert.True(first.IsSuccess);

            var restartedStore = new SqliteEventStore(databasePath, domain.EventTypes, serviceIdProvider: service);
            var rolledBackTime = new MutableTimeProvider(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var restartedGenerator = new MonotonicSortableUniqueIdGenerator(rolledBackTime);
            var restartedExecutor = new GeneralSekibanExecutor(
                restartedStore,
                new InMemoryObjectAccessor(restartedStore, domain),
                domain,
                null,
                null,
                restartedGenerator,
                new SortableUniqueIdSeedCoordinator(restartedGenerator),
                service);
            var afterRestart = await restartedExecutor.ExecuteAsync(
                new PathCommand(),
                (_, _) => Task.FromResult(EventOrNone.Event(new PathEvent("after"), new PathTag("after"))));
            Assert.True(afterRestart.IsSuccess);

            Assert.True(string.CompareOrdinal(
                afterRestart.GetValue().SortableUniqueId,
                first.GetValue().SortableUniqueId) > 0);
            var catchUp = (await restartedStore.ReadAllSerializableEventsAsync(
                new SortableUniqueId(first.GetValue().SortableUniqueId!))).GetValue().ToArray();
            Assert.Single(catchUp);
            Assert.Equal(afterRestart.GetValue().SortableUniqueId, catchUp[0].SortableUniqueIdValue);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static DcbDomainTypes BuildDomain()
    {
        var domain = DomainType.GetDomainTypes();
        ((SimpleEventTypes)domain.EventTypes).RegisterEventType<PathEvent>();
        ((SimpleTagTypes)domain.TagTypes).RegisterTagGroupType<PathTag>();
        ((SimpleTagTypes)domain.TagTypes).RegisterTagGroupType<ConsistencyPathTag>();
        return domain;
    }

    private static SerializableEventCandidate Candidate(DcbDomainTypes domain, string value) =>
        new(
            JsonSerializer.SerializeToUtf8Bytes(new PathEvent(value), domain.JsonSerializerOptions),
            nameof(PathEvent),
            [$"Path:{value}"]);

    private static SerializableEvent Persisted(DcbDomainTypes domain, string value, long ticks) =>
        new(
            JsonSerializer.SerializeToUtf8Bytes(new PathEvent(value), domain.JsonSerializerOptions),
            SortableUniqueId.Generate(new DateTime(ticks, DateTimeKind.Utc), Guid.NewGuid()),
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), "seed", "test"),
            [$"Path:{value}"],
            nameof(PathEvent));

    private static long ReadTicks(string id) => long.Parse(
        id.AsSpan(0, SortableUniqueId.TickNumberOfLength),
        System.Globalization.CultureInfo.InvariantCulture);

    private sealed record PathCommand : ICommand;
    private sealed record PathEvent(string Value) : IEventPayload;

    private sealed record PathTag(string Id) : IStringTagGroup<PathTag>
    {
        public static string TagGroupName => "Path";
        public static PathTag FromContent(string content) => new(content);
        public bool IsConsistencyTag() => false;
        public string GetId() => Id;
    }

    private sealed record ConsistencyPathTag(string Id) : IStringTagGroup<ConsistencyPathTag>
    {
        public static string TagGroupName => "ConsistencyPath";
        public static ConsistencyPathTag FromContent(string content) => new(content);
        public bool IsConsistencyTag() => true;
        public string GetId() => Id;
    }

    private sealed class FrozenTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class MutableServiceIdProvider(string current) : IServiceIdProvider
    {
        public string Current { get; set; } = current;
        public string GetCurrentServiceId() => Current;
    }

    private sealed class RecordingGenerator(ISortableUniqueIdGenerator inner) : ISortableUniqueIdGenerator
    {
        private readonly object _lock = new();
        private readonly List<string> _ids = [];
        public int GenerateCalls { get; private set; }
        public IReadOnlyList<string> GeneratedIds { get { lock (_lock) return _ids.ToArray(); } }
        public string GenerateNew()
        {
            var id = inner.GenerateNew();
            lock (_lock)
            {
                GenerateCalls++;
                _ids.Add(id);
            }
            return id;
        }
        public void Seed(long ticks) => inner.Seed(ticks);
    }

    private sealed class CountingActorAccessor : IActorObjectAccessor
    {
        private int _calls;
        private int _reservationCalls;
        public int Calls => Volatile.Read(ref _calls);
        public int ReservationCalls => Volatile.Read(ref _reservationCalls);
        public Task<ResultBox<T>> GetActorAsync<T>(string actorId) where T : class
        {
            Interlocked.Increment(ref _calls);
            if (typeof(T) == typeof(ITagConsistentActorCommon))
            {
                return Task.FromResult(ResultBox.FromValue((T)(object)new CountingTagConsistentActor(this, actorId)));
            }
            return Task.FromResult(ResultBox.Error<T>(new InvalidOperationException("unexpected actor type")));
        }
        public Task<bool> ActorExistsAsync(string actorId)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(false);
        }

        private sealed class CountingTagConsistentActor(CountingActorAccessor owner, string actorId)
            : ITagConsistentActorCommon
        {
            public Task<string> GetTagActorIdAsync() => Task.FromResult(actorId);
            public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() =>
                Task.FromResult(ResultBox.FromValue(string.Empty));
            public Task<ResultBox<TagWriteReservation>> MakeReservationAsync(string? lastSortableUniqueId)
            {
                Interlocked.Increment(ref owner._reservationCalls);
                return Task.FromResult(ResultBox.FromValue(
                    new TagWriteReservation("reservation", DateTime.MaxValue.ToString("O"), actorId)));
            }
            public Task<bool> ConfirmReservationAsync(TagWriteReservation reservation) => Task.FromResult(true);
            public Task<bool> CancelReservationAsync(TagWriteReservation reservation) => Task.FromResult(true);
            public Task NotifyEventWrittenAsync() => Task.CompletedTask;
        }
    }

    private sealed class CountingConditionalStore(
        InMemoryConditionalEventStore inner,
        IServiceIdProvider? serviceIdProvider = null)
        : IEventStore, IConditionalEventStore, IWriteConditionCapabilityProvider
    {
        private int _headReads;
        private int _writeCalls;
        public bool FailHeadReads { get; set; }
        public string? HeadOverride { get; set; }
        public int HeadReads => Volatile.Read(ref _headReads);
        public int WriteCalls => Volatile.Read(ref _writeCalls);
        public Dictionary<string, int> HeadReadsByService { get; } = new(StringComparer.Ordinal);

        public WriteConditionCapabilityDescriptor DescribeWriteConditions() => inner.DescribeWriteConditions();
        public Task<ResultBox<ConditionalAppendReceipt>> AppendIfUniqueAsync(
            ConditionalAppendRequest request,
            CancellationToken cancellationToken = default) => inner.AppendIfUniqueAsync(request, cancellationToken);
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync()
        {
            Interlocked.Increment(ref _headReads);
            if (serviceIdProvider is not null)
            {
                var serviceId = serviceIdProvider.GetCurrentServiceId();
                lock (HeadReadsByService)
                {
                    HeadReadsByService[serviceId] = HeadReadsByService.GetValueOrDefault(serviceId) + 1;
                }
            }
            return FailHeadReads
                ? Task.FromResult(ResultBox.Error<string>(new InvalidOperationException("head unavailable")))
                : HeadOverride is not null
                    ? Task.FromResult(ResultBox.FromValue(HeadOverride))
                    : inner.GetLatestSortableUniqueIdAsync();
        }
        public Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
            WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events)
        {
            Interlocked.Increment(ref _writeCalls);
            return inner.WriteSerializableEventsAsync(events);
        }
        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => inner.ReadTagsAsync(tag);
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => inner.GetLatestTagAsync(tag);
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => inner.TagExistsAsync(tag);
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => inner.GetEventCountAsync(since);
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null) => inner.GetAllTagsAsync(tagGroup);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) =>
            inner.ReadAllSerializableEventsAsync(since);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
            SortableUniqueId? since,
            int? maxCount) => inner.ReadAllSerializableEventsAsync(since, maxCount);
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId) =>
            inner.ReadSerializableEventAsync(eventId);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(
            ITag tag,
            SortableUniqueId? since = null) => inner.ReadSerializableEventsByTagAsync(tag, since);
    }
}
