using Dcb.Domain;
using Dcb.Domain.Student;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.CosmosDb.Repair;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Tests.Cosmos;

// Linked into emulator tests: the same public-API assertions run against both engines.
internal sealed class CosmosTagHeadScenarios : IDisposable
{
    private sealed class Service : IServiceIdProvider { public string GetCurrentServiceId() => "svc"; }
    private sealed record TestTag(string Content) : ITag
    {
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => "Student";
        public string GetTagContent() => Content;
    }
    private readonly CosmosDbContext _context;
    private readonly DefaultCosmosContainerResolver _resolver;
    private readonly CosmosDbEventStore _store;
    private readonly TestTag _tag = new(Guid.NewGuid().ToString("N"));
    private readonly CosmosDbEventStoreOptions _options;
    private Container _tags = null!;
    private string Tag => ((ITag)_tag).GetTag();
    private string Pk => "svc|" + Tag;
    public CosmosTagHeadScenarios(CosmosClient client, string database, CosmosDbEventStoreOptions? options = null)
    {
        _options = options ?? new() { TagHeadMode = CosmosTagHeadMode.Advance };
        _context = new(client, database, null, _options);
        _resolver = new(_options);
        _store = new(_context, DomainType.GetDomainTypes().EventTypes, new Service(), _resolver);
    }
    private Event Event(int index) => new(new StudentCreated(Guid.NewGuid(), "n", 5),
        SortableUniqueId.Generate(new DateTime(2026, 1, 1).AddSeconds(index), Guid.Empty),
        nameof(StudentCreated), Guid.NewGuid(), new EventMetadata("c", "c", "test"), [Tag]);
    private async Task InitializeAsync() => _tags = await _context.GetTagsContainerAsync(_resolver.ResolveTagsContainer("svc"));
    private async Task WriteAsync(IEnumerable<Event> events)
    {
        var result = await _store.WriteEventsAsync(events);
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
    }
    private async Task AssertAsync(List<Event> events)
    {
        var maximum = events.Max(e => e.SortableUniqueIdValue);
        if (_options.TagHeadMode == CosmosTagHeadMode.Advance)
            Assert.Equal(maximum, (await _tags.ReadItemAsync<JObject>("$head", new PartitionKey(Pk))).Resource["position"]!.ToString());
        else
            Assert.Equal(System.Net.HttpStatusCode.NotFound,
                (await Assert.ThrowsAsync<CosmosException>(() => _tags.ReadItemAsync<JObject>("$head", new PartitionKey(Pk)))).StatusCode);
        var rows = (await _store.ReadTagsAsync(_tag)).GetValue().ToList();
        Assert.Equal(events.OrderBy(e => e.SortableUniqueIdValue).Select(e => e.Id), rows.Select(r => r.EventId));
        Assert.Equal(maximum, (await _store.GetLatestTagAsync(_tag)).GetValue().LastSortedUniqueId);
        Assert.Equal(events.Count, Assert.Single((await _store.GetAllTagsAsync()).GetValue()).EventCount);
    }
    public async Task WritesAsync(int count, bool conflict, bool existingHead)
    {
        await InitializeAsync();
        var events = Enumerable.Range(1, count).Select(Event).ToList();
        if (conflict)
            await _tags.CreateItemAsync(CosmosTag.FromEventTag(Tag, "Student", events[0].SortableUniqueIdValue,
                events[0].Id, nameof(StudentCreated), "svc"), new PartitionKey(Pk));
        if (existingHead)
            await _tags.CreateItemAsync(new JObject
            {
                ["id"] = "$head", ["pk"] = Pk, ["serviceId"] = "svc", ["tag"] = Tag,
                ["documentType"] = "tagHead", ["position"] = Event(0).SortableUniqueIdValue
            }, new PartitionKey(Pk));
        await WriteAsync(events);
        await AssertAsync(events);
        var newer = Event(count + 1);
        await WriteAsync([newer]); events.Add(newer);
        var old = Event(0);
        await WriteAsync([old]); events.Add(old);
        await AssertAsync(events);
    }
    public async Task ConcurrentAsync()
    {
        await InitializeAsync();
        var events = Enumerable.Range(1, 12).Select(Event).ToList();
        await Task.WhenAll(events.Select(e => WriteAsync([e])));
        await AssertAsync(events);
    }
    public async Task OldRepairAsync(bool fallback)
    {
        await InitializeAsync();
        _options.TagHeadMode = CosmosTagHeadMode.Off;
        var top = Event(10); var old = Event(1);
        await WriteAsync(fallback ? [top] : [top, old]);
        _options.TagHeadMode = CosmosTagHeadMode.Advance;
        if (fallback)
        {
            await _tags.CreateItemAsync(CosmosTag.FromEventTag(Tag, "Student", old.SortableUniqueIdValue,
                old.Id, nameof(StudentCreated), "svc"), new PartitionKey(Pk));
            await WriteAsync([old]);
        }
        else
        {
            await _tags.DeleteItemAsync<JObject>(old.Id.ToString(), new PartitionKey(Pk));
            var repair = await new CosmosDbTagRepairServiceFactory(_context, _resolver).CreateAsync("svc");
            var report = await repair.RepairAsync(new CosmosTagRepairOptions { DryRun = false });
            Assert.Equal(1, report.Repaired);
        }
        await AssertAsync([old, top]);
    }
    // Repairs one missing old row on a tag that has rows but no head, and returns the reported request charge.
    public async Task<double> RepairChargeAsync(CosmosTagHeadMode mode)
    {
        await InitializeAsync();
        _options.TagHeadMode = CosmosTagHeadMode.Off;
        var top = Event(10); var old = Event(1);
        await WriteAsync([top, old]);
        await _tags.DeleteItemAsync<JObject>(old.Id.ToString(), new PartitionKey(Pk));
        _options.TagHeadMode = mode;
        var repair = await new CosmosDbTagRepairServiceFactory(_context, _resolver).CreateAsync("svc");
        var report = await repair.RepairAsync(new CosmosTagRepairOptions { DryRun = false });
        Assert.Equal(1, report.Repaired);
        return report.RequestCharge;
    }
    public async Task InvalidPositionAsync(bool serialized)
    {
        var invalid = Event(1) with { SortableUniqueIdValue = "123' OR true" };
        if (serialized)
        {
            var e = new SerializableEvent([], invalid.SortableUniqueIdValue, invalid.Id,
                invalid.EventMetadata, invalid.Tags, invalid.EventType);
            var result = await _store.WriteSerializableEventsAsync([e]);
            Assert.IsType<ArgumentException>(result.GetException());
        }
        else
        {
            var result = await _store.WriteEventsAsync([invalid]);
            Assert.IsType<ArgumentException>(result.GetException());
        }
    }
    public void Dispose() => _context.Dispose();
}
