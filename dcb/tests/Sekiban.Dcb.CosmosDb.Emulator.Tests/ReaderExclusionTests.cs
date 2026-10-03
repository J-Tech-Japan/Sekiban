using Dcb.Domain;
using Dcb.Domain.Student;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb.Repair;
using Sekiban.Dcb.CosmosDb.Sweep;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.CosmosDb.Emulator.Tests;

[Collection(CosmosEmulatorCollection.Name)]
public sealed class ReaderExclusionTests(CosmosEmulatorFixture fixture)
{
    private const string ServiceId = "svc";
    private sealed class Service : IServiceIdProvider { public string GetCurrentServiceId() => ServiceId; }
    private sealed record TestTag(string Content) : ITag
    {
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => "Student";
        public string GetTagContent() => Content;
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task EveryReaderExcludesHead(int rowCount)
    {
        await using var db = await TestDatabase.CreateAsync(fixture);
        var options = new CosmosDbEventStoreOptions { MaxItemCountPerPage = 1, MaxConcurrentTaggedStreamPointReads = 1 };
        using var context = new CosmosDbContext(db.Client, db.Name, null, options);
        var resolver = new DefaultCosmosContainerResolver(options);
        var store = new CosmosDbEventStore(context, DomainType.GetDomainTypes().EventTypes, new Service(), resolver);
        var tag = new TestTag(Guid.NewGuid().ToString("N"));
        var tagString = ((ITag)tag).GetTag();
        var events = Enumerable.Range(0, rowCount).Select(i => new Event(
            new StudentCreated(Guid.NewGuid(), "n", 5),
            SortableUniqueId.Generate(new DateTime(2026, 1, 1, 0, 0, i, DateTimeKind.Utc), Guid.Empty),
            nameof(StudentCreated), Guid.NewGuid(), new EventMetadata("c", "c", "test"), [tagString])).ToList();
        var write = await store.WriteEventsAsync(events);
        Assert.True(write.IsSuccess, write.IsSuccess ? "" : write.GetException().ToString());
        // Initialize both containers even when no event was written.
        await new CosmosDbTagRepairServiceFactory(context, resolver).CreateAsync(ServiceId);
        var tags = db.Client.GetContainer(db.Name, options.TagsContainerName);
        var pk = ServiceId + "|" + tagString;
        await tags.CreateItemAsync(EngineSemanticsTests.Head(pk), new PartitionKey(pk));

        Assert.Equal(rowCount > 0, (await store.TagExistsAsync(tag)).GetValue());
        Assert.Equal(events.Select(e => e.Id), (await store.ReadTagsAsync(tag)).GetValue().Select(r => r.EventId));
        Assert.Equal(events.LastOrDefault()?.SortableUniqueIdValue ?? string.Empty,
            (await store.GetLatestTagAsync(tag)).GetValue().LastSortedUniqueId);
        foreach (var group in new string?[] { null, "Student", "Other" })
        {
            var infos = (await store.GetAllTagsAsync(group)).GetValue().ToList();
            if (rowCount == 0 || group == "Other") { Assert.Empty(infos); continue; }
            var info = Assert.Single(infos);
            Assert.Equal(rowCount, info.EventCount);
            Assert.Equal(events[0].SortableUniqueIdValue, info.FirstSortableUniqueId);
            Assert.Equal(events[^1].SortableUniqueIdValue, info.LastSortableUniqueId);
        }
        var boundary = new SortableUniqueId(SortableUniqueId.Generate(new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc), Guid.Empty));
        foreach (var since in new SortableUniqueId?[] { null, boundary })
        {
            var expected = events.Where(e => since == null || string.CompareOrdinal(e.SortableUniqueIdValue, since.Value) > 0).ToList();
            Assert.Equal(expected.Select(e => e.Id), (await store.ReadEventsByTagAsync(tag, since)).GetValue().Select(e => e.Id));
            Assert.Equal(expected.Select(e => e.Id), (await store.ReadSerializableEventsByTagAsync(tag, since)).GetValue().Select(e => e.Id));
            foreach (var until in new SortableUniqueId?[] { null, boundary })
            {
                var streamed = new List<Guid>();
                var result = await store.StreamSerializableEventsByTagAsync(tag, since, until,
                    e => { streamed.Add(e.Id); return ValueTask.CompletedTask; });
                Assert.True(result.IsSuccess);
                Assert.Equal(expected.Where(e => until == null || string.CompareOrdinal(e.SortableUniqueIdValue, until.Value) <= 0).Select(e => e.Id), streamed);
            }
        }
        Assert.Equal(events.Select(e => e.Id), (await store.ReadAllEventsAsync()).GetValue().Select(e => e.Id));
        Assert.Equal(rowCount, (await store.GetEventCountAsync()).GetValue());
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task RepairAndSweepLeaveHeadAndRowsUnchanged(int rowCount)
    {
        await using var db = await TestDatabase.CreateAsync(fixture);
        var options = new CosmosDbEventStoreOptions();
        using var context = new CosmosDbContext(db.Client, db.Name, null, options);
        var resolver = new DefaultCosmosContainerResolver(options);
        var factory = new CosmosDbTagRepairServiceFactory(context, resolver);
        var repair = await factory.CreateAsync(ServiceId);
        var store = new CosmosDbEventStore(context, DomainType.GetDomainTypes().EventTypes, new Service(), resolver);
        var tag = "Student:maintenance";
        var events = Enumerable.Range(0, rowCount).Select(i => new Event(
            new StudentCreated(Guid.NewGuid(), "n", 5), SortableUniqueId.GenerateNew(),
            nameof(StudentCreated), Guid.NewGuid(), new EventMetadata("c", "c", "test"), [tag])).ToList();
        Assert.True((await store.WriteEventsAsync(events)).IsSuccess);
        var tags = db.Client.GetContainer(db.Name, options.TagsContainerName);
        await tags.CreateItemAsync(EngineSemanticsTests.Head(ServiceId + "|" + tag), new PartitionKey(ServiceId + "|" + tag));
        var before = await SnapshotAsync(tags);
        var report = await repair.RepairAsync(new CosmosTagRepairOptions { DryRun = false });
        Assert.Equal(rowCount, report.EventsScanned);
        Assert.Equal(rowCount, report.Present);
        Assert.Equal(0, report.Repaired);
        Assert.Equal(0, report.Missing);
        Assert.Equal(before, await SnapshotAsync(tags));

        // Use the public registration path and wait for the sweep's terminal log event (no internals access).
        var services = new ServiceCollection();
        var logs = new SweepLog();
        services.AddSingleton<ILogger<CosmosTagSweepService>>(logs);
        services.AddSekibanDcbCosmosDb($"AccountEndpoint={fixture.Endpoint};AccountKey={CosmosEmulatorFixture.Key};", db.Name);
        services.AddSekibanDcbCosmosDbTagSweep(sweep =>
        {
            sweep.Enabled = true;
            sweep.MaxStartupJitter = TimeSpan.Zero;
            sweep.Window = TimeSpan.FromDays(365);
        });
        services.Replace(ServiceDescriptor.Singleton(options));
        services.Replace(ServiceDescriptor.Singleton(context));
        services.Replace(ServiceDescriptor.Singleton<IServiceIdProvider>(new Service()));
        using var provider = services.BuildServiceProvider();
        var sweep = provider.GetServices<IHostedService>().OfType<CosmosTagSweepService>().Single();
        await sweep.StartAsync(CancellationToken.None);
        try { await logs.Finished.Task.WaitAsync(TimeSpan.FromSeconds(60)); }
        finally { await sweep.StopAsync(CancellationToken.None); }
        Assert.Contains(1, logs.EventIds); // Completed, not a swallowed repair failure or budget expiry.
        Assert.DoesNotContain(4, logs.EventIds);
        Assert.Equal(before, await SnapshotAsync(tags));
    }

    private static async Task<string[]> SnapshotAsync(Container container) =>
        (await EngineSemanticsTests.QueryAsync<JObject>(container, "SELECT * FROM c"))
        .OrderBy(j => j["id"]!.ToString()).Select(j => j.ToString(Newtonsoft.Json.Formatting.None)).ToArray();

    private sealed class SweepLog : ILogger<CosmosTagSweepService>
    {
        // Event IDs 1-4 are the sweep's terminal outcomes: completed, needs attention, budget exhausted, failed.
        private readonly object _gate = new();
        private readonly List<int> _eventIds = [];
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<int> EventIds { get { lock (_gate) return _eventIds.ToArray(); } }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_gate) _eventIds.Add(eventId.Id);
            if (eventId.Id is >= 1 and <= 4) Finished.TrySetResult();
        }
    }
}
