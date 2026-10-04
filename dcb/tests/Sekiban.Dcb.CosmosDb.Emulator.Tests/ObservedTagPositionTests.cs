using Dcb.Domain;
using Dcb.Domain.Student;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.CosmosDb.Emulator.Tests;

[Collection(CosmosEmulatorCollection.Name)]
public sealed class ObservedTagPositionTests(CosmosEmulatorFixture fixture)
{
    [SkippableTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task RecordingOption_PersistsEvidenceAndReadersReturnOriginalEvent(bool recording)
    {
        await using var db = await TestDatabase.CreateAsync(fixture);
        var domain = DomainType.GetDomainTypes();
        var options = new CosmosDbEventStoreOptions { RecordObservedTagPositions = recording };
        using var context = new CosmosDbContext(db.Client, db.Name, options: options);
        var resolver = new DefaultCosmosContainerResolver(options);
        var store = new CosmosDbEventStore(context, domain.EventTypes, new DefaultServiceIdProvider(), resolver);
        var ev = new Event(new StudentCreated(Guid.NewGuid(), "observed", 5), SortableUniqueId.GenerateNew(),
            nameof(StudentCreated), Guid.NewGuid(), new EventMetadata("cause", "correlation", "user"), ["Student:observed"])
            .ToSerializableEvent(domain.EventTypes);
        var result = await store.WriteSerializableEventsWithObservedTagPositionsAsync([ev],
            new Dictionary<string, string?> { ["Student:observed"] = "" });
        Assert.True(result.IsSuccess, result.IsSuccess ? "" : result.GetException().ToString());
        var container = await context.GetEventsContainerAsync(resolver.ResolveEventsContainer(DefaultServiceIdProvider.DefaultServiceId));
        var doc = (await container.ReadItemAsync<JObject>(ev.Id.ToString(),
            new PartitionKey(DefaultServiceIdProvider.DefaultServiceId + "|" + ev.Id))).Resource;
        if (recording)
        {
            Assert.Equal("", doc["observed"]![0]!.Value<string>());
            Assert.True(Guid.TryParseExact(doc.Value<string>("observedWrite"), "N", out _));
        }
        else
        {
            Assert.Null(doc["observed"]);
            Assert.Null(doc["observedWrite"]);
        }
        var read = (await store.ReadSerializableEventAsync(ev.Id)).GetValue();
        Assert.Equal(ev.Payload, read.Payload);
        Assert.Equal(ev.Tags, read.Tags);
        Assert.Equal(ev.EventMetadata, read.EventMetadata);
    }
}
