using Amazon.DynamoDBv2.DocumentModel;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.History;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using Sekiban.Infrastructure.Cosmos.Lib.Json;
using Sekiban.Infrastructure.Postgres.Databases;
using System.Text;
using System.Text.Json;
using DynamoDocument = Amazon.DynamoDBv2.DocumentModel.Document;

if (args.Length != 2 || (args[0] != "write" && args[0] != "verify"))
{
    Console.Error.WriteLine("usage: CompatibilityWorker write|verify <vector-path>");
    return 2;
}

try
{
    if (args[0] == "write")
    {
        var vector = DurableVector.Create();
        DurableVector.Verify(vector);
        await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(vector, DurableVector.FileOptions));
        Console.WriteLine($"wrote and verified durable vector: {args[1]}");
    }
    else
    {
        var vector = JsonSerializer.Deserialize<DurableVector>(await File.ReadAllTextAsync(args[1]), DurableVector.FileOptions)
            ?? throw new InvalidOperationException("durable vector was empty");
        DurableVector.Verify(vector);
        Console.WriteLine($"verified durable vector: {args[1]}");
    }

    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"durable serialization compatibility failure: {exception}");
    return 1;
}

public interface ICompatibilityShape
{
    string Label { get; }
}

public sealed record DerivedCompatibilityShape(string Label, int Count, string? Optional) : ICompatibilityShape;

public sealed record DerivedCompatibilityEvent(string Label, int Count, string? Optional) : IEventPayloadCommon, ICompatibilityShape;

public sealed record DurableVector
{
    public const string Schema = "sek-g49-durable-v1";
    public static readonly JsonSerializerOptions FileOptions = new() { WriteIndented = true };

    public string SchemaVersion { get; init; } = Schema;
    public string RuntimeTypeName { get; init; } = string.Empty;
    public string EventJson { get; init; } = string.Empty;
    public string CasingEventJson { get; init; } = string.Empty;
    public string CosmosEventJson { get; init; } = string.Empty;
    public string DynamoEventJson { get; init; } = string.Empty;
    public string PostgresEventJson { get; init; } = string.Empty;
    public string SnapshotJson { get; init; } = string.Empty;
    public string CosmosSnapshotJson { get; init; } = string.Empty;
    public string DynamoSnapshotJson { get; init; } = string.Empty;
    public string PostgresSnapshotJson { get; init; } = string.Empty;

    public static DurableVector Create()
    {
        var eventPayload = new DerivedCompatibilityEvent("derived-event", 17, null);
        var eventDocument = Event.GenerateEvent(
            new Guid("11111111-1111-1111-1111-111111111111"),
            new Guid("22222222-2222-2222-2222-222222222222"),
            "compatibility-partition",
            DocumentType.Event,
            nameof(DerivedCompatibilityEvent),
            new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            "0000000000000000000000000000000000000001",
            eventPayload,
            "CompatibilityAggregate",
            42,
            "compatibility-root",
            [new CallHistory(new Guid("33333333-3333-3333-3333-333333333333"), nameof(DerivedCompatibilityEvent), null)]);

        var snapshot = new SnapshotDocument
        {
            Id = new Guid("44444444-4444-4444-4444-444444444444"),
            AggregateId = new Guid("22222222-2222-2222-2222-222222222222"),
            PartitionKey = "compatibility-snapshot-partition",
            DocumentType = DocumentType.AggregateSnapshot,
            DocumentTypeName = nameof(DerivedCompatibilityShape),
            TimeStamp = new DateTime(2025, 1, 2, 3, 4, 6, DateTimeKind.Utc),
            SortableUniqueId = "0000000000000000000000000000000000000002",
            AggregateType = "CompatibilityAggregate",
            RootPartitionKey = "compatibility-root",
            Snapshot = new DerivedCompatibilityShape("derived-snapshot", 23, null),
            LastEventId = new Guid("55555555-5555-5555-5555-555555555555"),
            LastSortableUniqueId = "0000000000000000000000000000000000000003",
            SavedVersion = 43,
            PayloadVersionIdentifier = "compatibility-v1"
        };

        var eventJson = RequiredJson(eventDocument);
        var snapshotJson = RequiredJson(snapshot);
        var cosmos = new SekibanCosmosSerializer();
        var postgresEvent = DbEvent.FromEvent(eventDocument);
        var postgresSnapshot = DbSingleProjectionSnapshotDocument.FromDocument(snapshot, AggregateContainerGroup.Default);

        return new DurableVector
        {
            RuntimeTypeName = typeof(DerivedCompatibilityEvent).AssemblyQualifiedName
                ?? throw new InvalidOperationException("derived event type did not have an assembly-qualified name"),
            EventJson = eventJson,
            CasingEventJson = eventJson
                .Replace("\"Payload\"", "\"pAyLoAd\"", StringComparison.Ordinal)
                .Replace("\"Label\"", "\"lAbEl\"", StringComparison.Ordinal)
                .Replace("\"Count\"", "\"cOuNt\"", StringComparison.Ordinal),
            CosmosEventJson = StreamText(cosmos.ToStream(eventDocument)),
            DynamoEventJson = DynamoDocument.FromJson(eventJson).ToJson(),
            PostgresEventJson = JsonSerializer.Serialize(postgresEvent, FileOptions),
            SnapshotJson = snapshotJson,
            CosmosSnapshotJson = StreamText(cosmos.ToStream(snapshot)),
            DynamoSnapshotJson = DynamoDocument.FromJson(snapshotJson).ToJson(),
            PostgresSnapshotJson = JsonSerializer.Serialize(postgresSnapshot, FileOptions)
        };
    }

    public static void Verify(DurableVector vector)
    {
        Require(vector.SchemaVersion == Schema, "unexpected durable vector schema");
        Require(Type.GetType(vector.RuntimeTypeName) == typeof(DerivedCompatibilityEvent), "runtime Type did not resolve to the derived event type");
        Require(!vector.EventJson.Contains("\"Optional\"", StringComparison.Ordinal), "null event property was not omitted by Sekiban JSON options");

        VerifyEvent(DeserializeEvent(vector.EventJson), "Sekiban event");
        VerifyEvent(DeserializeEvent(vector.CasingEventJson), "case-insensitive Sekiban event");

        var cosmos = new SekibanCosmosSerializer();
        using (var eventStream = Utf8Stream(vector.CosmosEventJson))
        {
            VerifyEvent(cosmos.FromStream<Event<DerivedCompatibilityEvent>>(eventStream), "Cosmos serializer event");
        }

        VerifyEvent(DeserializeEvent(DynamoDocument.FromJson(vector.DynamoEventJson).ToJson()), "Dynamo JSON event");

        var postgresEvent = JsonSerializer.Deserialize<DbEvent>(vector.PostgresEventJson, FileOptions)
            ?? throw new InvalidOperationException("Postgres event vector was empty");
        VerifyEventPayload(
            SekibanJsonHelper.Deserialize(postgresEvent.Payload, typeof(DerivedCompatibilityEvent)) as DerivedCompatibilityEvent,
            "Postgres DbEvent payload");
        Require(postgresEvent.Version == 42 && postgresEvent.DocumentTypeName == nameof(DerivedCompatibilityEvent), "Postgres event metadata changed");

        VerifySnapshot(DeserializeSnapshot(vector.SnapshotJson), "Sekiban snapshot");
        using (var snapshotStream = Utf8Stream(vector.CosmosSnapshotJson))
        {
            VerifySnapshot(cosmos.FromStream<SnapshotDocument>(snapshotStream), "Cosmos serializer snapshot");
        }
        VerifySnapshot(DeserializeSnapshot(DynamoDocument.FromJson(vector.DynamoSnapshotJson).ToJson()), "Dynamo JSON snapshot");

        var postgresSnapshot = JsonSerializer.Deserialize<DbSingleProjectionSnapshotDocument>(vector.PostgresSnapshotJson, FileOptions)
            ?? throw new InvalidOperationException("Postgres snapshot vector was empty");
        VerifyShape(
            SekibanJsonHelper.Deserialize(postgresSnapshot.Snapshot, typeof(DerivedCompatibilityShape)) as DerivedCompatibilityShape,
            "Postgres snapshot payload");
        Require(
            postgresSnapshot.LastEventId == new Guid("55555555-5555-5555-5555-555555555555") &&
            postgresSnapshot.LastSortableUniqueId == "0000000000000000000000000000000000000003" &&
            postgresSnapshot.SavedVersion == 43,
            "Postgres snapshot metadata changed");
    }

    private static Event<DerivedCompatibilityEvent> DeserializeEvent(string json) =>
        SekibanJsonHelper.Deserialize(json, typeof(Event<DerivedCompatibilityEvent>)) as Event<DerivedCompatibilityEvent>
        ?? throw new InvalidOperationException("event vector did not deserialize as the derived event");

    private static SnapshotDocument DeserializeSnapshot(string json) =>
        SekibanJsonHelper.Deserialize<SnapshotDocument>(json)
        ?? throw new InvalidOperationException("snapshot vector did not deserialize");

    private static void VerifyEvent(Event<DerivedCompatibilityEvent> eventDocument, string seam)
    {
        VerifyEventPayload(eventDocument.Payload, seam);
        Require(
            eventDocument.Id == new Guid("11111111-1111-1111-1111-111111111111") &&
            eventDocument.AggregateId == new Guid("22222222-2222-2222-2222-222222222222") &&
            eventDocument.Version == 42 &&
            eventDocument.DocumentTypeName == nameof(DerivedCompatibilityEvent) &&
            eventDocument.CallHistories.Count == 1,
            $"{seam} event metadata changed");
    }

    private static void VerifyEventPayload(DerivedCompatibilityEvent? payload, string seam)
    {
        var verifiedPayload = payload ?? throw new InvalidOperationException($"{seam} payload was null");
        ICompatibilityShape interfaceShape = verifiedPayload;
        Require(
            interfaceShape.GetType() == typeof(DerivedCompatibilityEvent) &&
            interfaceShape.Label == "derived-event" &&
            verifiedPayload.Count == 17 &&
            verifiedPayload.Optional is null,
            $"{seam} interface/derived runtime Type or null semantics changed");
    }

    private static void VerifySnapshot(SnapshotDocument snapshot, string seam)
    {
        VerifyShape(
            SekibanJsonHelper.ConvertTo(snapshot.Snapshot, typeof(DerivedCompatibilityShape)) as DerivedCompatibilityShape,
            seam);
        Require(
            snapshot.Id == new Guid("44444444-4444-4444-4444-444444444444") &&
            snapshot.LastEventId == new Guid("55555555-5555-5555-5555-555555555555") &&
            snapshot.LastSortableUniqueId == "0000000000000000000000000000000000000003" &&
            snapshot.SavedVersion == 43 &&
            snapshot.PayloadVersionIdentifier == "compatibility-v1",
            $"{seam} snapshot metadata changed");
    }

    private static void VerifyShape(DerivedCompatibilityShape? shape, string seam)
    {
        var verifiedShape = shape ?? throw new InvalidOperationException($"{seam} snapshot shape was null");
        ICompatibilityShape interfaceShape = verifiedShape;
        Require(
            interfaceShape.GetType() == typeof(DerivedCompatibilityShape) &&
            interfaceShape.Label == "derived-snapshot" &&
            verifiedShape.Count == 23 &&
            verifiedShape.Optional is null,
            $"{seam} snapshot interface/derived runtime Type or null semantics changed");
    }

    private static string RequiredJson(object value) =>
        SekibanJsonHelper.Serialize(value) ?? throw new InvalidOperationException("Sekiban JSON serialization returned null");

    private static string StreamText(Stream stream)
    {
        using (stream)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, false, leaveOpen: false);
            return reader.ReadToEnd();
        }
    }

    private static MemoryStream Utf8Stream(string value) => new(Encoding.UTF8.GetBytes(value));

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
