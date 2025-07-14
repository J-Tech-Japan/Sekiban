using ResultBoxes;
using Sekiban.Pure.Documents;
using System.IO.Compression;
using System.Text.Json;
namespace Sekiban.Pure.Aggregates;

[Serializable]
public record SerializableAggregate
{
    public Guid AggregateId { get; init; } = Guid.Empty;
    public string Group { get; init; } = PartitionKeys.DefaultAggregateGroupName;
    public string RootPartitionKey { get; init; } = PartitionKeys.DefaultRootPartitionKey;
    
    public int Version { get; init; }
    public string LastSortableUniqueId { get; init; } = string.Empty;
    public string ProjectorVersion { get; init; } = string.Empty;
    public string ProjectorTypeName { get; init; } = string.Empty;
    public string PayloadTypeName { get; init; } = string.Empty;
    
    public byte[] CompressedPayloadJson { get; init; } = Array.Empty<byte>();
    
    public string PayloadAssemblyVersion { get; init; } = string.Empty;
    
    public SerializableAggregate() { }

    public PartitionKeys GetPartitionKeys() => new(AggregateId, Group, RootPartitionKey);

    private SerializableAggregate(
        Guid aggregateId,
        string group,
        string rootPartitionKey,
        int version,
        string lastSortableUniqueId,
        string projectorVersion,
        string projectorTypeName,
        string payloadTypeName,
        byte[] compressedPayloadJson,
        string payloadAssemblyVersion)
    {
        AggregateId = aggregateId;
        Group = group;
        RootPartitionKey = rootPartitionKey;
        Version = version;
        LastSortableUniqueId = lastSortableUniqueId;
        ProjectorVersion = projectorVersion;
        ProjectorTypeName = projectorTypeName;
        PayloadTypeName = payloadTypeName;
        CompressedPayloadJson = compressedPayloadJson;
        PayloadAssemblyVersion = payloadAssemblyVersion;
    }

    public static async Task<SerializableAggregate> CreateFromAsync(
        Aggregate aggregate, 
        JsonSerializerOptions options)
    {
        byte[] compressedPayloadJson = Array.Empty<byte>();
        string payloadAssemblyVersion = "0.0.0.0";

        if (aggregate.Payload != null)
        {
            var payloadType = aggregate.Payload.GetType();
            payloadAssemblyVersion = payloadType.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            
            var payloadJson = JsonSerializer.SerializeToUtf8Bytes(
                aggregate.Payload, 
                payloadType, 
                options);
            
            compressedPayloadJson = await CompressAsync(payloadJson);
        }

        return new SerializableAggregate(
            aggregate.PartitionKeys.AggregateId,
            aggregate.PartitionKeys.Group,
            aggregate.PartitionKeys.RootPartitionKey,
            aggregate.Version,
            aggregate.LastSortableUniqueId,
            aggregate.ProjectorVersion,
            aggregate.ProjectorTypeName,
            aggregate.PayloadTypeName,
            compressedPayloadJson,
            payloadAssemblyVersion
        );
    }

    public async Task<OptionalValue<Aggregate>> ToAggregateAsync(
        SekibanDomainTypes domainTypes)
    {
        try
        {
            if (PayloadTypeName == typeof(EmptyAggregatePayload).Name)
            {
                var emptyAggregate = new Aggregate(
                    new EmptyAggregatePayload(),
                    GetPartitionKeys(),
                    Version,
                    LastSortableUniqueId,
                    ProjectorVersion,
                    ProjectorTypeName,
                    PayloadTypeName);

                return new OptionalValue<Aggregate>(emptyAggregate);
            }
            
            Type? payloadType = null;
            try
            {
                payloadType = domainTypes.AggregateTypes.GetPayloadTypeByName(PayloadTypeName);
                if (payloadType == null)
                {
                    return OptionalValue<Aggregate>.Empty;
                }
            }
            catch
            {
                return OptionalValue<Aggregate>.Empty;
            }

            /*
            var currentVersion = payloadType.Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            if (currentVersion != PayloadAssemblyVersion)
            {
                return OptionalValue<Aggregate>.Empty;
            }
            */

            IAggregatePayload? payload = null;
            
            if (CompressedPayloadJson.Length > 0)
            {
                var decompressedJson = await DecompressAsync(CompressedPayloadJson);
                payload = (IAggregatePayload?)JsonSerializer.Deserialize(
                    decompressedJson, 
                    payloadType, 
                    domainTypes.JsonSerializerOptions);
                
                if (payload == null)
                {
                    return OptionalValue<Aggregate>.Empty;
                }
            }
            else
            {
                payload = new EmptyAggregatePayload();
            }

            var aggregate = new Aggregate(
                payload,
                GetPartitionKeys(),
                Version,
                LastSortableUniqueId,
                ProjectorVersion,
                ProjectorTypeName,
                PayloadTypeName);

            return new OptionalValue<Aggregate>(aggregate);
        }
        catch (Exception)
        {
            return OptionalValue<Aggregate>.Empty;
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
