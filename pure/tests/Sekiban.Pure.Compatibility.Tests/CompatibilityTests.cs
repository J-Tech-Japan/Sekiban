using Sekiban.Pure.Aggregates;
using Sekiban.Pure.CosmosDb;
using Sekiban.Pure.Events;
using Sekiban.Pure.Orleans.Parts;
using System.Text.Json;
using Xunit;

namespace CompatibilityDomain;

public class CompatibilityTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Theory]
    [InlineData("event-envelope.json")]
    [InlineData("aggregate-snapshot.json")]
    [InlineData("orleans-system-text-json.json")]
    [InlineData("orleans-newtonsoft-json.json")]
    [InlineData("cosmos-event-document.json")]
    public async Task Serialization_matches_900_bytes(string name)
    {
        var actual = (await FixedInputs.SerializeAll())[name];
        Assert.Equal(Fixture(name), actual);
    }

    [Fact]
    public void Reads_event_envelope()
    {
        var ev = JsonSerializer.Deserialize<Event<AccountRegistered>>(Fixture("event-envelope.json"), FixedInputs.DomainTypes.JsonSerializerOptions);
        Assert.Equal(FixedInputs.Event, ev);
    }

    [Theory]
    [InlineData("aggregate-snapshot.json")]
    [InlineData("orleans-system-text-json.json")]
    [InlineData("orleans-newtonsoft-json.json")]
    public async Task Reads_snapshot_and_grain_storage(string name)
    {
        var bytes = Fixture(name);
        var snapshot = name switch
        {
            "orleans-system-text-json.json" => new SystemTextJsonStorageSerializer().Deserialize<SerializableAggregate>(new BinaryData(bytes)),
            "orleans-newtonsoft-json.json" => new NewtonsoftJsonSekibanOrleansSerializer().Deserialize<SerializableAggregate>(new BinaryData(bytes)),
            _ => JsonSerializer.Deserialize<SerializableAggregate>(bytes, FixedInputs.DomainTypes.JsonSerializerOptions)!
        };
        Assert.Equal(FixedInputs.AggregateId, snapshot.AggregateId);
        Assert.Equal("AccountProjector", snapshot.Group);
        Assert.Equal("compatibility", snapshot.RootPartitionKey);
        Assert.Equal(1, snapshot.Version);
        Assert.Equal(FixedInputs.SortableId, snapshot.LastSortableUniqueId);
        Assert.Equal("1.0.0", snapshot.ProjectorVersion);
        Assert.Equal("AccountProjector", snapshot.ProjectorTypeName);
        Assert.Equal("Account", snapshot.PayloadTypeName);
        Assert.Equal("1.0.0.0", snapshot.PayloadAssemblyVersion);
        var restored = await snapshot.ToAggregateAsync(FixedInputs.DomainTypes);
        Assert.True(restored.HasValue);
        Assert.Equal(FixedInputs.Aggregate, restored.Value);
    }

    [Fact]
    public void Reads_cosmos_document()
    {
        var serializer = new SekibanCosmosSerializer(FixedInputs.DomainTypes.JsonSerializerOptions);
        var document = serializer.FromStream<EventDocument<AccountRegistered>>(new MemoryStream(Fixture("cosmos-event-document.json")));
        Assert.Equal(FixedInputs.CosmosDocument, document);
        Assert.Equal(FixedInputs.EventId, document.Id);
        Assert.Equal(FixedInputs.AggregateId, document.AggregateId);
        Assert.Equal("compatibility@AccountProjector@11111111-2222-3333-4444-555555555555", document.PartitionKey);
        Assert.Equal(new Sekiban.Pure.Documents.SortableUniqueIdValue(FixedInputs.SortableId).GetTicks(), document.TimeStamp);
    }
}
