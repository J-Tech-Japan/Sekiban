using CompatibilityDomain.Generated;
using Sekiban.Pure;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.CosmosDb;
using Sekiban.Pure.Orleans.Parts;
using System.Text.Json;

namespace CompatibilityDomain;

public static class FixedInputs
{
    public static readonly Guid AggregateId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    public static readonly Guid EventId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    public static readonly DateTime Timestamp = new(2025, 9, 22, 12, 34, 56, DateTimeKind.Utc);
    public const string SortableId = "06388890769600000000012345678901";
    public const string Name = "Sample account 日本語 <>&";
    public static SekibanDomainTypes DomainTypes => CompatibilityDomainDomainTypes.Generate();
    public static RegisterAccount Command => new(AggregateId, Name, Timestamp);
    public static Event<AccountRegistered> Event => new(EventId,
        new AccountRegistered(Command.Name, Command.RegisteredAt),
        Command.SpecifyPartitionKeys(Command), SortableId, 1,
        new EventMetadata("22222222-3333-4444-5555-666666666666", "compatibility-correlation", "fixture-user"));
    public static Aggregate Aggregate => Aggregate.EmptyFromPartitionKeys(Event.PartitionKeys)
        .Project(Event, new AccountProjector()).GetValue();
    public static Task<SerializableAggregate> Snapshot() =>
        SerializableAggregate.CreateFromAsync(Aggregate, DomainTypes.JsonSerializerOptions);
    public static EventDocument<AccountRegistered> CosmosDocument => EventDocument<AccountRegistered>.FromEvent(Event);

    public static async Task<Dictionary<string, byte[]>> SerializeAll()
    {
        var options = DomainTypes.JsonSerializerOptions;
        var snapshot = await Snapshot();
        var cosmosSerializer = new SekibanCosmosSerializer(options);
        using var cosmosStream = cosmosSerializer.ToStream(CosmosDocument);
        using var cosmosBytes = new MemoryStream();
        await cosmosStream.CopyToAsync(cosmosBytes);
        return new()
        {
            ["event-envelope.json"] = JsonSerializer.SerializeToUtf8Bytes(Event, options),
            ["aggregate-snapshot.json"] = JsonSerializer.SerializeToUtf8Bytes(snapshot, options),
            ["orleans-system-text-json.json"] = new SystemTextJsonStorageSerializer().Serialize(snapshot).ToArray(),
            ["orleans-newtonsoft-json.json"] = new NewtonsoftJsonSekibanOrleansSerializer().Serialize(snapshot).ToArray(),
            ["cosmos-event-document.json"] = cosmosBytes.ToArray()
        };
    }
}
