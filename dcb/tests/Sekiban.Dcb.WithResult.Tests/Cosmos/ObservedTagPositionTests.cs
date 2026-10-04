using Dcb.Domain;
using Dcb.Domain.Student;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using System.Net;

namespace Sekiban.Dcb.Tests.Cosmos;

public sealed class ObservedTagPositionTests
{
    private static readonly DcbDomainTypes Domain = DomainType.GetDomainTypes();
    private const string Position = "000000000000000000000000000123";
    private static SerializableEvent Event(params string[] tags) => new Sekiban.Dcb.Events.Event(
        new StudentCreated(Guid.NewGuid(), "observed", 5), SortableUniqueId.GenerateNew(), nameof(StudentCreated),
        Guid.NewGuid(), new EventMetadata("cause", "correlation", "user"), tags.ToList()).ToSerializableEvent(Domain.EventTypes);

    private static (CosmosDbEventStore Store, InMemoryCosmosClient Client, CosmosDbContext Context) Setup(bool enabled)
    {
        var client = new InMemoryCosmosClient();
        var options = new CosmosDbEventStoreOptions { RecordObservedTagPositions = enabled, MaxConcurrentEventWrites = 1 };
        var context = new CosmosDbContext(client, options: options);
        return (new CosmosDbEventStore(context, Domain.EventTypes, new DefaultServiceIdProvider(), new DefaultCosmosContainerResolver(options)), client, context);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task AlignedOriginalObservations_OneIdPerCall_AndReaderCompatibility(bool enabled)
    {
        var (store, client, context) = Setup(enabled);
        using (context)
        {
            var events = new[] { Event("Student:a", "Student:b", "Student:c"), Event("Student:c", "Student:a") };
            var observations = new Dictionary<string, string?> { ["Student:a"] = Position, ["Student:b"] = "" };
            Assert.Equal(enabled, store.RecordsObservedTagPositions);
            Assert.True((await store.WriteSerializableEventsWithObservedTagPositionsAsync(events, observations)).IsSuccess);
            var docs = client.Container("events").Items;
            Assert.Equal(2, docs.Count);
            foreach (var doc in docs)
            {
                var ev = events.Single(e => e.Id.ToString() == doc.Value<string>("id"));
                if (enabled)
                {
                    Assert.Equal(ev.Tags.Select(t => observations.GetValueOrDefault(t)),
                        doc["observed"]!.ToObject<string?[]>());
                    Assert.True(Guid.TryParseExact(doc.Value<string>("observedWrite"), "N", out _));
                }
                else
                {
                    var expected = JObject.FromObject(CosmosEventDocumentMapper.FromSerializableEvent(ev,
                        DefaultServiceIdProvider.DefaultServiceId, doc.Value<DateTime>("timestamp")));
                    foreach (var systemField in new[] { "_etag", "_ts" }) { expected.Remove(systemField); }
                    var actual = (JObject)doc.DeepClone();
                    foreach (var systemField in new[] { "_etag", "_ts" }) { actual.Remove(systemField); }
                    Assert.Equal(expected.ToString(Formatting.None), actual.ToString(Formatting.None));
                    Assert.Null(doc["observed"]);
                    Assert.Null(doc["observedWrite"]);
                }
                var read = (await store.ReadSerializableEventAsync(ev.Id)).GetValue();
                Assert.Equal(ev.Payload, read.Payload);
                Assert.Equal(ev.Tags, read.Tags);
                Assert.Equal(ev.EventMetadata, read.EventMetadata);
            }
            if (enabled)
            {
                var writeId = docs[0].Value<string>("observedWrite");
                Assert.All(docs, d => Assert.Equal(writeId, d.Value<string>("observedWrite")));
                Assert.True((await store.WriteSerializableEventsWithObservedTagPositionsAsync([Event("Student:a")], observations)).IsSuccess);
                Assert.NotEqual(writeId, client.Container("events").Items.Last().Value<string>("observedWrite"));
            }
        }
    }

    [Fact]
    public async Task AllNullOrMissingObservations_OmitBothProperties()
    {
        var (store, client, context) = Setup(true);
        using (context)
        {
            Assert.True((await store.WriteSerializableEventsWithObservedTagPositionsAsync(
                [Event("Student:a", "Student:b")], new Dictionary<string, string?> { ["Student:a"] = null })).IsSuccess);
            var doc = Assert.Single(client.Container("events").Items);
            Assert.Null(doc["observed"]);
            Assert.Null(doc["observedWrite"]);
        }
    }

    [Theory]
    [InlineData("bad")] [InlineData("00000000000000000000000000012x")]
    public async Task InvalidObservation_RejectsBeforeAnyWrite(string invalid)
    {
        var (store, client, context) = Setup(true);
        using (context)
        {
            var result = await store.WriteSerializableEventsWithObservedTagPositionsAsync([Event("Student:a")],
                new Dictionary<string, string?> { ["Student:a"] = invalid });
            Assert.IsType<ArgumentException>(result.GetException());
            Assert.Empty(client.Container("events").Items);
            Assert.Empty(client.Container("tags").Items);
        }
    }

    [Fact]
    public async Task PartialFailure_PreservesOriginalEvidenceOnSurvivors_AndRetryGetsNewId()
    {
        var (store, client, context) = Setup(true);
        using (context)
        {
            client.Container("events").WriteFaults.Enqueue(new CosmosException("failed", HttpStatusCode.BadRequest, 0, "test", 0));
            var observations = new Dictionary<string, string?> { ["Student:a"] = Position };
            var result = await store.WriteSerializableEventsWithObservedTagPositionsAsync(
                [Event("Student:a"), Event("Student:a"), Event("Student:a")], observations);
            Assert.IsType<CosmosPartialEventWriteException>(result.GetException());
            var survivors = client.Container("events").Items;
            Assert.Equal(2, survivors.Count);
            Assert.Empty(client.Container("tags").Items);
            var writeId = survivors[0].Value<string>("observedWrite");
            Assert.All(survivors, d =>
            {
                Assert.Equal(Position, d["observed"]![0]!.Value<string>());
                Assert.Equal(writeId, d.Value<string>("observedWrite"));
            });
            Assert.True((await store.WriteSerializableEventsWithObservedTagPositionsAsync([Event("Student:a")], observations)).IsSuccess);
            Assert.NotEqual(writeId, client.Container("events").Items.Last().Value<string>("observedWrite"));
        }
    }

    [Fact]
    public void NullOmission_IsPerProperty_WhileArrayNullsRemain()
    {
        var document = CosmosEventDocumentMapper.FromSerializableEvent(Event("Student:a", "Student:b"), "service", DateTime.UtcNow);
        var off = JObject.Parse(JsonConvert.SerializeObject(document, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Include }));
        Assert.Null(off["observed"]);
        Assert.NotNull(off["_etag"]);
        CosmosEventDocumentMapper.ApplyObservations(document, new Dictionary<string, string?> { ["Student:a"] = "" }, new string('f', 32));
        var on = JObject.Parse(JsonConvert.SerializeObject(document));
        Assert.Equal(JTokenType.Null, on["observed"]![1]!.Type);
    }
}
