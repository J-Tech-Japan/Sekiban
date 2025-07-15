using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using System.IO.Compression;
using System.Text.Json;

namespace Sekiban.Pure.Command.Executor;

[Serializable]
public record SerializableCommandResponse
{
    public Guid AggregateId { get; init; } = Guid.Empty;
    public string Group { get; init; } = PartitionKeys.DefaultAggregateGroupName;
    public string RootPartitionKey { get; init; } = PartitionKeys.DefaultRootPartitionKey;
    
    public int Version { get; init; }
    
    public List<SerializableEvent> Events { get; init; } = new();
    
    public SerializableCommandResponse() { }

    public PartitionKeys GetPartitionKeys() => new(AggregateId, Group, RootPartitionKey);

    private SerializableCommandResponse(
        Guid aggregateId,
        string group,
        string rootPartitionKey,
        int version,
        List<SerializableEvent> events)
    {
        AggregateId = aggregateId;
        Group = group;
        RootPartitionKey = rootPartitionKey;
        Version = version;
        Events = events;
    }

    public static async Task<SerializableCommandResponse> CreateFromAsync(
        CommandResponse response,
        JsonSerializerOptions options)
    {
        var serializableEvents = new List<SerializableEvent>();
        
        foreach (var @event in response.Events)
        {
            var serializableEvent = await SerializableEvent.CreateFromAsync(@event, options);
            serializableEvents.Add(serializableEvent);
        }

        return new SerializableCommandResponse(
            response.PartitionKeys.AggregateId,
            response.PartitionKeys.Group,
            response.PartitionKeys.RootPartitionKey,
            response.Version,
            serializableEvents
        );
    }

    public async Task<OptionalValue<CommandResponse>> ToCommandResponseAsync(
        SekibanDomainTypes domainTypes)
    {
        try
        {
            var events = new List<IEvent>();
            
            foreach (var serializableEvent in Events)
            {
                var eventOptional = await serializableEvent.ToEventAsync(domainTypes);
                if (!eventOptional.HasValue)
                {
                    return OptionalValue<CommandResponse>.Empty;
                }
                events.Add(eventOptional.Value!);
            }

            var response = new CommandResponse(
                GetPartitionKeys(),
                events,
                Version
            );

            return new OptionalValue<CommandResponse>(response);
        }
        catch (Exception)
        {
            return OptionalValue<CommandResponse>.Empty;
        }
    }

    [Serializable]
    public record SerializableEvent
    {
        public string EventTypeName { get; init; } = string.Empty;
        public string SortableUniqueId { get; init; } = string.Empty;
        public byte[] CompressedPayloadJson { get; init; } = Array.Empty<byte>();
        public string PayloadAssemblyVersion { get; init; } = string.Empty;
        
        public Guid AggregateId { get; init; } = Guid.Empty;
        public string Group { get; init; } = PartitionKeys.DefaultAggregateGroupName;
        public string RootPartitionKey { get; init; } = PartitionKeys.DefaultRootPartitionKey;
        public int Version { get; init; }
        
        public SerializableEvent() { }

        private SerializableEvent(
            string eventTypeName,
            string sortableUniqueId,
            byte[] compressedPayloadJson,
            string payloadAssemblyVersion,
            Guid aggregateId,
            string group,
            string rootPartitionKey,
            int version)
        {
            EventTypeName = eventTypeName;
            SortableUniqueId = sortableUniqueId;
            CompressedPayloadJson = compressedPayloadJson;
            PayloadAssemblyVersion = payloadAssemblyVersion;
            AggregateId = aggregateId;
            Group = group;
            RootPartitionKey = rootPartitionKey;
            Version = version;
        }

        public static async Task<SerializableEvent> CreateFromAsync(
            IEvent @event,
            JsonSerializerOptions options)
        {
            var payload = @event.GetPayload();
            var payloadType = payload.GetType();
            var payloadAssemblyVersion = payloadType.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            
            var payloadJson = JsonSerializer.SerializeToUtf8Bytes(
                payload,
                payloadType,
                options);
            
            var compressedPayloadJson = await CompressAsync(payloadJson);

            return new SerializableEvent(
                payloadType.Name,
                @event.SortableUniqueId,
                compressedPayloadJson,
                payloadAssemblyVersion,
                @event.PartitionKeys.AggregateId,
                @event.PartitionKeys.Group,
                @event.PartitionKeys.RootPartitionKey,
                @event.Version
            );
        }

        public async Task<OptionalValue<IEvent>> ToEventAsync(
            SekibanDomainTypes domainTypes)
        {
            try
            {
                // Get the payload type by name
                var payloadType = domainTypes.EventTypes.GetEventTypeByName(EventTypeName);
                if (payloadType == null)
                {
                    return OptionalValue<IEvent>.Empty;
                }

                var decompressedJson = await DecompressAsync(CompressedPayloadJson);
                var payload = JsonSerializer.Deserialize(
                    decompressedJson,
                    payloadType,
                    domainTypes.JsonSerializerOptions) as IEventPayload;
                
                if (payload == null)
                {
                    return OptionalValue<IEvent>.Empty;
                }

                var partitionKeys = new PartitionKeys(AggregateId, Group, RootPartitionKey);
                
                // Get the Event type with the correct payload type
                var genericEventType = typeof(Event<>).MakeGenericType(payload.GetType());
                var @event = Activator.CreateInstance(
                    genericEventType,
                    Guid.NewGuid(), // Id
                    payload,
                    partitionKeys,
                    SortableUniqueId,
                    Version,
                    new EventMetadata(string.Empty, string.Empty, string.Empty)) as IEvent;

                if (@event == null)
                {
                    return OptionalValue<IEvent>.Empty;
                }

                return new OptionalValue<IEvent>(@event);
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
}
