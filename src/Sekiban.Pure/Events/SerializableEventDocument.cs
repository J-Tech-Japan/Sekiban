using ResultBoxes;
using Sekiban.Pure.Documents;
using System.IO.Compression;
using System.Text.Json;
namespace Sekiban.Pure.Events;

[Serializable]
public record SerializableEventDocument
{
    public Guid Id { get; init; } = Guid.Empty;
    public string SortableUniqueId { get; init; } = string.Empty;
    public int Version { get; init; }

    public Guid AggregateId { get; init; } = Guid.Empty;
    public string AggregateGroup { get; init; } = PartitionKeys.DefaultAggregateGroupName;
    public string RootPartitionKey { get; init; } = PartitionKeys.DefaultRootPartitionKey;

    public string PayloadTypeName { get; init; } = string.Empty;
    public DateTime TimeStamp { get; init; }
    public string PartitionKey { get; init; } = string.Empty;

    public string CausationId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string ExecutedUser { get; init; } = string.Empty;

    public byte[] CompressedPayloadJson { get; init; } = Array.Empty<byte>();

    public string PayloadAssemblyVersion { get; init; } = string.Empty;

    public SerializableEventDocument() { }

    private SerializableEventDocument(
        Guid id,
        string sortableUniqueId,
        int version,
        Guid aggregateId,
        string aggregateGroup,
        string rootPartitionKey,
        string payloadTypeName,
        DateTime timeStamp,
        string partitionKey,
        string causationId,
        string correlationId,
        string executedUser,
        byte[] compressedPayloadJson,
        string payloadAssemblyVersion)
    {
        Id = id;
        SortableUniqueId = sortableUniqueId;
        Version = version;
        AggregateId = aggregateId;
        AggregateGroup = aggregateGroup;
        RootPartitionKey = rootPartitionKey;
        PayloadTypeName = payloadTypeName;
        TimeStamp = timeStamp;
        PartitionKey = partitionKey;
        CausationId = causationId;
        CorrelationId = correlationId;
        ExecutedUser = executedUser;
        CompressedPayloadJson = compressedPayloadJson;
        PayloadAssemblyVersion = payloadAssemblyVersion;
    }

    public PartitionKeys GetPartitionKeys() => new(AggregateId, AggregateGroup, RootPartitionKey);

    public EventMetadata GetEventMetadata() => new(CausationId, CorrelationId, ExecutedUser);

    public static async Task<SerializableEventDocument> CreateFromAsync<TEventPayload>(
        EventDocument<TEventPayload> eventDoc,
        JsonSerializerOptions options) where TEventPayload : IEventPayload
    {
        var compressedPayloadJson = Array.Empty<byte>();
        var payloadAssemblyVersion = "0.0.0.0";

        if (eventDoc.Payload != null)
        {
            var payloadType = eventDoc.Payload.GetType();
            payloadAssemblyVersion = payloadType.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

            var payloadJson = JsonSerializer.SerializeToUtf8Bytes(eventDoc.Payload, payloadType, options);

            compressedPayloadJson = await CompressAsync(payloadJson);
        }

        return new SerializableEventDocument(
            eventDoc.Id,
            eventDoc.SortableUniqueId,
            eventDoc.Version,
            eventDoc.AggregateId,
            eventDoc.AggregateGroup,
            eventDoc.RootPartitionKey,
            eventDoc.PayloadTypeName,
            eventDoc.TimeStamp,
            eventDoc.PartitionKey,
            eventDoc.Metadata.CausationId,
            eventDoc.Metadata.CorrelationId,
            eventDoc.Metadata.ExecutedUser,
            compressedPayloadJson,
            payloadAssemblyVersion);
    }

    public static async Task<SerializableEventDocument> CreateFromEventAsync(IEvent ev, JsonSerializerOptions options)
    {
        var sortableUniqueIdValue = new SortableUniqueIdValue(ev.SortableUniqueId);

        var compressedPayloadJson = Array.Empty<byte>();
        var payloadAssemblyVersion = "0.0.0.0";

        var payload = ev.GetPayload();
        if (payload != null)
        {
            var payloadType = payload.GetType();
            payloadAssemblyVersion = payloadType.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

            var payloadJson = JsonSerializer.SerializeToUtf8Bytes(payload, payloadType, options);

            compressedPayloadJson = await CompressAsync(payloadJson);
        }

        return new SerializableEventDocument(
            ev.Id,
            ev.SortableUniqueId,
            ev.Version,
            ev.PartitionKeys.AggregateId,
            ev.PartitionKeys.Group,
            ev.PartitionKeys.RootPartitionKey,
            payload?.GetType().Name ?? string.Empty,
            sortableUniqueIdValue.GetTicks(),
            ev.PartitionKeys.ToPrimaryKeysString(),
            ev.Metadata.CausationId,
            ev.Metadata.CorrelationId,
            ev.Metadata.ExecutedUser,
            compressedPayloadJson,
            payloadAssemblyVersion);
    }

    public async Task<OptionalValue<IEventDocument>> ToEventDocumentAsync(SekibanDomainTypes domainTypes)
    {
        try
        {
            Type? payloadType = null;
            try
            {
                payloadType = domainTypes.EventTypes.GetEventTypeByName(PayloadTypeName);
                if (payloadType == null)
                {
                    return OptionalValue<IEventDocument>.Empty;
                }
            }
            catch
            {
                return OptionalValue<IEventDocument>.Empty;
            }

            IEventPayload? payload = null;

            if (CompressedPayloadJson.Length > 0)
            {
                var decompressedJson = await DecompressAsync(CompressedPayloadJson);
                payload = (IEventPayload?)JsonSerializer.Deserialize(
                    decompressedJson,
                    payloadType,
                    domainTypes.JsonSerializerOptions);

                if (payload == null)
                {
                    return OptionalValue<IEventDocument>.Empty;
                }
            } else
            {
                return OptionalValue<IEventDocument>.Empty;
            }

            var eventDocumentType = typeof(EventDocument<>).MakeGenericType(payloadType);
            var metadata = GetEventMetadata();

            var eventDocument = Activator.CreateInstance(
                eventDocumentType,
                Id,
                payload,
                SortableUniqueId,
                Version,
                AggregateId,
                AggregateGroup,
                RootPartitionKey,
                PayloadTypeName,
                TimeStamp,
                PartitionKey,
                metadata) as IEventDocument;

            if (eventDocument == null)
            {
                return OptionalValue<IEventDocument>.Empty;
            }

            return new OptionalValue<IEventDocument>(eventDocument);
        }
        catch (Exception)
        {
            return OptionalValue<IEventDocument>.Empty;
        }
    }

    public async Task<OptionalValue<IEvent>> ToEventAsync(SekibanDomainTypes domainTypes)
    {
        try
        {
            Type? payloadType = null;
            try
            {
                payloadType = domainTypes.EventTypes.GetEventTypeByName(PayloadTypeName);
                if (payloadType == null)
                {
                    return OptionalValue<IEvent>.Empty;
                }
            }
            catch
            {
                return OptionalValue<IEvent>.Empty;
            }

            IEventPayload? payload = null;

            if (CompressedPayloadJson.Length > 0)
            {
                var decompressedJson = await DecompressAsync(CompressedPayloadJson);
                payload = (IEventPayload?)JsonSerializer.Deserialize(
                    decompressedJson,
                    payloadType,
                    domainTypes.JsonSerializerOptions);

                if (payload == null)
                {
                    return OptionalValue<IEvent>.Empty;
                }
            } else
            {
                return OptionalValue<IEvent>.Empty;
            }

            var eventType = typeof(Event<>).MakeGenericType(payloadType);
            var partitionKeys = GetPartitionKeys();
            var metadata = GetEventMetadata();

            var ev = Activator.CreateInstance(
                eventType,
                Id,
                payload,
                partitionKeys,
                SortableUniqueId,
                Version,
                metadata) as IEvent;

            if (ev == null)
            {
                return OptionalValue<IEvent>.Empty;
            }

            return new OptionalValue<IEvent>(ev);
        }
        catch (Exception)
        {
            return OptionalValue<IEvent>.Empty;
        }
    }

    private static async Task<byte[]> CompressAsync(byte[] data)
    {
        if (data.Length == 0)
        {
            return Array.Empty<byte>();
        }

        using var memoryStream = new MemoryStream();
        using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Fastest))
        {
            await gzipStream.WriteAsync(data);
        }
        return memoryStream.ToArray();
    }

    private static async Task<byte[]> DecompressAsync(byte[] compressedData)
    {
        if (compressedData.Length == 0)
        {
            return Array.Empty<byte>();
        }

        using var compressedStream = new MemoryStream(compressedData);
        using var decompressedStream = new MemoryStream();
        using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
        {
            await gzipStream.CopyToAsync(decompressedStream);
        }
        return decompressedStream.ToArray();
    }
}
