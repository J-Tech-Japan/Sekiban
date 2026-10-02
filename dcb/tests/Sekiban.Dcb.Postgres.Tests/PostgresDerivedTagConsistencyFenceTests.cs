using Dcb.Domain.Student;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.TagConsistencyFence;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;

namespace Sekiban.Dcb.Postgres.Tests;

public sealed class PostgresDerivedTagConsistencyFenceTests(PostgresTestFixture fixture) : PostgresTestBase(fixture)
{
    private const string Service = DefaultServiceIdProvider.DefaultServiceId;
    private sealed record Command : ICommand;

    [Theory]
    [InlineData(false, TagConsistencyFenceMode.DeriveFromReservations)]
    [InlineData(true, TagConsistencyFenceMode.DeriveFromReservations)]
    [InlineData(false, TagConsistencyFenceMode.Off)]
    [InlineData(true, TagConsistencyFenceMode.Off)]
    public async Task SeparateReservationCaches_OnlyFencedWritersConflict(bool update, TagConsistencyFenceMode mode)
    {
        // Provision the same enablement epoch for both the fenced case and the legacy mutant.
        await using (var connection = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var epoch = new NpgsqlCommand(
                "INSERT INTO dcb_tag_head_enablement_epochs (\"ServiceId\", \"EnabledAtUtc\") VALUES (@service, @enabled)", connection);
            epoch.Parameters.AddWithValue("service", Service);
            epoch.Parameters.AddWithValue("enabled", DateTime.UtcNow);
            await epoch.ExecuteNonQueryAsync();
        }
        var store = new PostgresEventStore(Fixture.DbContextFactory, Fixture.DomainTypes.EventTypes, new DefaultServiceIdProvider());
        var id = Guid.NewGuid();
        var tag = new StudentTag(id);
        string? initialPosition = null;
        if (update)
        {
            var seed = new GeneralSekibanExecutor(store, new InMemoryObjectAccessor(store, Fixture.DomainTypes), Fixture.DomainTypes);
            var seedResult = await seed.ExecuteCommandAsync(ctx => ctx.AppendEvent(new StudentCreated(id, "seed"), tag));
            Assert.True(seedResult.IsSuccess, seedResult.IsSuccess ? "" : seedResult.GetException().ToString());
            initialPosition = seedResult.GetValue().SortableUniqueId;
        }

        var barrier = new WriteBarrierStore(store);
        // One store, two independent actor caches. DI enables the option through its public registration surface.
        using var firstProvider = Provider(barrier, mode);
        using var secondProvider = Provider(barrier, mode);
        var first = firstProvider.GetRequiredService<GeneralSekibanExecutor>();
        var second = secondProvider.GetRequiredService<GeneralSekibanExecutor>();
        async Task<ResultBox<EventOrNone>> Handler(Command _, ICommandContext context)
        {
            var exists = await context.TagExistsAsync(tag);
            Assert.True(exists.IsSuccess, exists.IsSuccess ? "" : exists.GetException().ToString());
            Assert.Equal(update, exists.GetValue());
            return await context.AppendEvent(new StudentCreated(id, "racing"), tag);
        }
        var firstWrite = first.ExecuteAsync(new Command(), Handler);
        var secondWrite = second.ExecuteAsync(new Command(), Handler);
        try
        {
            await barrier.BothArrived.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(2, barrier.Arrivals); // Reaching either write boundary proves both reservations succeeded.
            Assert.False(firstWrite.IsCompleted);
            Assert.False(secondWrite.IsCompleted);
        }
        finally
        {
            barrier.Release.TrySetResult();
        }
        var results = await Task.WhenAll(firstWrite, secondWrite).WaitAsync(TimeSpan.FromSeconds(30));
        var fenced = mode == TagConsistencyFenceMode.DeriveFromReservations;
        Assert.Equal(fenced ? 1 : 2, results.Count(r => r.IsSuccess));
        if (fenced)
        {
            var conflict = Assert.IsType<ExpectedTagPositionConflictException>(Assert.Single(results, r => !r.IsSuccess).GetException());
            var pair = Assert.Single(conflict.Pairs);
            Assert.Equal(tag.GetTag(), pair.Tag);
            Assert.Equal(update ? TagHeadExpectation.Exact(initialPosition!) : TagHeadExpectation.AssertEmpty(), pair.Expected);
            Assert.Equal(Assert.Single(results, r => r.IsSuccess).GetValue().SortableUniqueId, pair.ObservedPosition);
        }
        Assert.Equal(fenced ? 2 : 0, barrier.FencedArrivals);
        Assert.Equal(fenced ? 0 : 2, barrier.LegacyArrivals);
        await VerifyRowsAsync(tag.GetTag(), (update ? 1 : 0) + (fenced ? 1 : 2));

        if (fenced && update)
        {
            // Cancel alone leaves the loser's cached head stale. Conflict notification must make this retry refresh.
            var loser = results[0].IsSuccess ? second : first;
            var retry = await loser.ExecuteAsync(new Command(), Handler);
            Assert.True(retry.IsSuccess, retry.IsSuccess ? "" : retry.GetException().ToString());
            await VerifyRowsAsync(tag.GetTag(), 3);
        }
    }

    private ServiceProvider Provider(IEventStore store, TagConsistencyFenceMode mode)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Fixture.DomainTypes);
        services.AddSingleton(store);
        services.AddSingleton<IActorObjectAccessor>(new InMemoryObjectAccessor(store, Fixture.DomainTypes));
        services.AddSekibanDcbTagConsistencyFence(o => o.Mode = mode);
        services.AddTransient<GeneralSekibanExecutor>();
        return services.BuildServiceProvider();
    }

    private async Task VerifyRowsAsync(string tag, int expectedCount)
    {
        // Fresh physical connection: observe committed rows independently of the executors and their actor caches.
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT (SELECT COUNT(*) FROM dcb_events WHERE "ServiceId" = @service),
                   COUNT(*), MAX("SortableUniqueId"),
                   (SELECT "HeadPosition" FROM dcb_tag_heads WHERE "ServiceId" = @service AND "Tag" = @tag)
            FROM dcb_tags WHERE "ServiceId" = @service AND "Tag" = @tag
            """, connection);
        command.Parameters.AddWithValue("service", Service);
        command.Parameters.AddWithValue("tag", tag);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(expectedCount, reader.GetInt64(0));
        Assert.Equal(expectedCount, reader.GetInt64(1));
        Assert.Equal(reader.GetString(2), reader.GetString(3));
    }

    private sealed class WriteBarrierStore(PostgresEventStore inner) : IEventStore, IExpectedTagPositionEventStore,
        IWriteConditionCapabilityProvider
    {
        private int _arrivals, _fenced, _legacy;
        public int Arrivals => Volatile.Read(ref _arrivals);
        public int FencedArrivals => Volatile.Read(ref _fenced);
        public int LegacyArrivals => Volatile.Read(ref _legacy);
        public TaskCompletionSource BothArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private async Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrivals) == 2) BothArrived.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(25));
        }
        public WriteConditionCapabilityDescriptor DescribeWriteConditions() => inner.DescribeWriteConditions();
        public Task<ResultBox<bool>> EnsureExpectedTagPositionEnforcementEnabledAsync(CancellationToken ct = default) =>
            inner.EnsureExpectedTagPositionEnforcementEnabledAsync(ct);
        public async Task<ResultBox<ExpectedTagPositionWriteResult>> WriteSerializableEventsWithExpectedTagPositionsAsync(
            IReadOnlyList<SerializableEvent> events, ExpectedTagPositionSpecification specification, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _fenced);
            await ArriveAsync();
            return await inner.WriteSerializableEventsWithExpectedTagPositionsAsync(events, specification, ct);
        }
        public async Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(IEnumerable<SerializableEvent> events)
        {
            Interlocked.Increment(ref _legacy);
            await ArriveAsync();
            return await inner.WriteSerializableEventsAsync(events);
        }
        public Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag) => inner.ReadTagsAsync(tag);
        public Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag) => inner.GetLatestTagAsync(tag);
        public Task<ResultBox<bool>> TagExistsAsync(ITag tag) => inner.TagExistsAsync(tag);
        public Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null) => inner.GetEventCountAsync(since);
        public Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? group = null) => inner.GetAllTagsAsync(group);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null) => inner.ReadAllSerializableEventsAsync(since);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since, int? count) => inner.ReadAllSerializableEventsAsync(since, count);
        public Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid id) => inner.ReadSerializableEventAsync(id);
        public Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null) => inner.ReadSerializableEventsByTagAsync(tag, since);
        public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => inner.GetLatestSortableUniqueIdAsync();
    }
}
