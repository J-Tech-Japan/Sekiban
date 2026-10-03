using Dcb.Domain;
using Dcb.Domain.Student;
using Newtonsoft.Json.Linq;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.CosmosDb.Migration;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.CosmosDb.Repair;
using Sekiban.Dcb.CosmosDb.Sweep;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Tests.Cosmos;

public class CosmosTagReaderExclusionTests
{
    private const string ServiceId = "svc";
    private sealed class Service : IServiceIdProvider
    {
        public string GetCurrentServiceId() => ServiceId;
    }

    private sealed record TestTag(string Content) : ITag
    {
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => "Student";
        public string GetTagContent() => Content;
    }

    private sealed class Fixture
    {
        public readonly InMemoryCosmosClient Client = new();
        public readonly TestTag Tag = new("1");
        public readonly CosmosDbContext Context;
        public readonly DefaultCosmosContainerResolver Resolver;
        public readonly CosmosDbEventStore Store;
        public readonly List<Event> Events = new();
        public InMemoryCosmosContainer Tags => Client.Container("tags");
        public string Partition => ServiceId + "|" + ((ITag)Tag).GetTag();

        public Fixture()
        {
            var options = new CosmosDbEventStoreOptions { MaxItemCountPerPage = 1, MaxConcurrentTaggedStreamPointReads = 1 };
            Context = new CosmosDbContext(Client, "test-db", null, options);
            Resolver = new DefaultCosmosContainerResolver(options);
            Store = new CosmosDbEventStore(Context, DomainType.GetDomainTypes().EventTypes, new Service(), Resolver);
        }

        public async Task SeedAsync(int rowCount, bool overReturn)
        {
            for (var i = 0; i < rowCount; i++)
            {
                Events.Add(new Event(new StudentCreated(Guid.NewGuid(), "n", 5),
                    SortableUniqueId.Generate(new DateTime(2026, 1, 1, 0, 0, i, DateTimeKind.Utc), Guid.Empty),
                    nameof(StudentCreated), Guid.NewGuid(), new EventMetadata("c", "c", "test"),
                    new List<string> { ((ITag)Tag).GetTag() }));
            }
            Assert.True((await Store.WriteEventsAsync(Events)).IsSuccess);
            var head = new JObject
            {
                ["id"] = "$head", ["pk"] = Partition, ["serviceId"] = ServiceId,
                ["tag"] = ((ITag)Tag).GetTag(), ["documentType"] = "tagHead", ["position"] = "head-position"
            };
            Tags.Seed(head);
            if (overReturn)
            {
                // Even a discriminator-bearing document with a real event id must be rejected.
                head["id"] = "$disguised";
                head["eventId"] = Events[0].Id.ToString();
                head["sortableUniqueId"] = SortableUniqueId.Generate(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), Guid.Empty);
                Tags.Seed(head);
                head.Remove("documentType");
                head.Remove("eventId");
                head["id"] = "$missing-event";
                Tags.Seed(head);
            }
            Tags.IgnoreTagRowPredicate = overReturn;
        }

        public async Task AssertReadersAsync()
        {
            var rows = (await Store.ReadTagsAsync(Tag)).GetValue().ToList();
            Assert.Equal(Events.Select(e => e.Id), rows.Select(r => r.EventId));
            Assert.Equal(Events.LastOrDefault()?.SortableUniqueIdValue ?? string.Empty,
                (await Store.GetLatestTagAsync(Tag)).GetValue().LastSortedUniqueId);
            foreach (var group in new string?[] { null, "Student", "Other" })
            {
                var infos = (await Store.GetAllTagsAsync(group)).GetValue().ToList();
                if (Events.Count == 0 || group == "Other")
                {
                    Assert.Empty(infos);
                    continue;
                }
                var info = Assert.Single(infos);
                Assert.Equal(Events.Count, info.EventCount);
                Assert.Equal(Events[0].SortableUniqueIdValue, info.FirstSortableUniqueId);
                Assert.Equal(Events[^1].SortableUniqueIdValue, info.LastSortableUniqueId);
                var storedRows = Tags.Items.Where(r => r.Property("documentType") == null && r["eventId"] != null).ToList();
                Assert.Equal(storedRows.Min(r => r["createdAt"]!.Value<DateTime>()), info.FirstEventAt);
                Assert.Equal(storedRows.Max(r => r["createdAt"]!.Value<DateTime>()), info.LastEventAt);
            }
            var boundary = new SortableUniqueId(SortableUniqueId.Generate(new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc), Guid.Empty));
            foreach (var since in new SortableUniqueId?[] { null, boundary })
            {
                var expected = Events.Where(e => since == null || string.CompareOrdinal(e.SortableUniqueIdValue, since.Value) > 0).ToList();
                Assert.Equal(expected.Select(e => e.Id), (await Store.ReadEventsByTagAsync(Tag, since)).GetValue().Select(e => e.Id));
                Assert.Equal(expected.Select(e => e.Id), (await Store.ReadSerializableEventsByTagAsync(Tag, since)).GetValue().Select(e => e.Id));
                foreach (var until in new SortableUniqueId?[] { null, boundary })
                {
                    var streamed = new List<Guid>();
                    var result = await Store.StreamSerializableEventsByTagAsync(Tag, since, until, e =>
                    {
                        streamed.Add(e.Id);
                        return ValueTask.CompletedTask;
                    });
                    Assert.True(result.IsSuccess);
                    Assert.Equal(expected.Where(e => until == null || string.CompareOrdinal(e.SortableUniqueIdValue, until.Value) <= 0).Select(e => e.Id), streamed);
                }
            }
        }

        public async Task AssertMaintenanceAsync()
        {
            var creates = Tags.Creates;
            var before = Tags.Items.Select(r => r.ToString()).ToList();
            var factory = new CosmosDbTagRepairServiceFactory(Context, Resolver);
            var repair = await factory.CreateAsync(ServiceId);
            var report = await repair.RepairAsync(new CosmosTagRepairOptions { DryRun = false });
            Assert.Equal(Events.Count, report.Present);
            Assert.Equal(0, report.Repaired);
            using var sweep = new CosmosTagSweepService(
                new CosmosTagSweepOptions { Enabled = true, MaxStartupJitter = TimeSpan.Zero, Window = TimeSpan.FromDays(3650) },
                new[] { ServiceId }, new CosmosTagRepairRunner(factory));
            await sweep.StartAsync(CancellationToken.None);
            await sweep.Sweeping!;
            await sweep.StopAsync(CancellationToken.None);
            var migration = await new CosmosDbLegacyTagMigrationServiceFactory(Context, Resolver).CreateAsync(ServiceId);
            Assert.Empty((await migration.PlanAsync(new CosmosTagMigrationPlanOptions())).Actions);
            Assert.Equal(creates, Tags.Creates);
            Assert.Equal(0, Tags.Deletes);
            Assert.Equal(before, Tags.Items.Select(r => r.ToString()));
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public async Task ReadersAndMaintenanceIgnoreNonRows(int rows, bool overReturn)
    {
        var fixture = new Fixture();
        await fixture.SeedAsync(rows, overReturn);
        if (!overReturn)
        {
            Assert.Equal(rows > 0, (await fixture.Store.TagExistsAsync(fixture.Tag)).GetValue());
        }
        await fixture.AssertReadersAsync();
        await fixture.AssertMaintenanceAsync();
        Assert.All(fixture.Tags.QueryTexts, text => Assert.Contains(CosmosTagQueryFilters.RowsOnly, text));
    }

    [Fact]
    public async Task QueryInventoryCoversAllShapesAndPredicateFiltersBeforeMaterialization()
    {
        var fixture = new Fixture();
        await fixture.SeedAsync(3, false);
        await fixture.AssertReadersAsync();
        await fixture.Store.TagExistsAsync(fixture.Tag);
        await fixture.AssertMaintenanceAsync();
        var texts = fixture.Tags.QueryTexts.Distinct().ToList();
        Assert.Equal(10, texts.Count); // four index bounds, tags, latest, count, two service/group shapes, repair/migration lookup
        Assert.All(texts, text => Assert.Contains(CosmosTagQueryFilters.RowsOnly, text));
        var query = new Microsoft.Azure.Cosmos.QueryDefinition(
            "SELECT * FROM c WHERE c.pk = @pk AND " + CosmosTagQueryFilters.RowsOnly).WithParameter("@pk", fixture.Partition);
        using var iterator = fixture.Tags.GetItemQueryIterator<JObject>(query);
        var documents = await iterator.ReadNextAsync();
        Assert.Equal(3, documents.Count);
        Assert.All(documents, row => Assert.Null(row.Property("documentType")));
    }

    [Fact]
    public void LegacyMatcherDoesNotRecognizeHeadAndGuardPreservesInvalidGuidBehavior()
    {
        var derived = CosmosTag.FromEventTag("Student:1", "Student", SortableUniqueId.GenerateNew(), Guid.NewGuid(), "event", ServiceId);
        Assert.Null(JObject.FromObject(derived).Property("documentType"));
        var head = new CosmosTag { Id = "$head", Pk = derived.Pk, ServiceId = ServiceId, Tag = derived.Tag };
        Assert.Equal(LegacyRowMatch.NotOurKey, CosmosLegacyTagRowMatcher.Classify(head, derived, out _));
        Assert.Null(CosmosTagQueryFilters.ReadRow(new JObject { ["documentType"] = JValue.CreateNull(), ["eventId"] = derived.EventId }));
        Assert.NotNull(CosmosTagQueryFilters.ReadRow(new JObject { ["eventId"] = "invalid-guid" }));
    }
}
