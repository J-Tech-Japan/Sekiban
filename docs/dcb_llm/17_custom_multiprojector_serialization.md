# Custom MultiProjector Serialization

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Commands, Events, Tags, Projectors](03_aggregate_command_events.md)
> - [MultiProjection](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Command Workflow](06_workflow.md)
> - [Serialization & Domain Types](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client UI (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Storage Providers](11_dapr_setup.md)
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)
> - [Custom MultiProjector Serialization](17_custom_multiprojector_serialization.md) (You are here)

MultiProjector states can become large. Custom serialization allows you to optimize the storage format and
control compression for better performance.

## SerializationResult Record

When implementing custom serialization, the `Serialize` method returns a `SerializationResult`:

```csharp
public record SerializationResult(
    byte[] Data,              // Serialized data (compression controlled by serializer)
    long OriginalSizeBytes,   // Size before compression
    long CompressedSizeBytes  // Size after compression (same as OriginalSizeBytes if not compressed)
)
{
    public double CompressionRatio => OriginalSizeBytes > 0
        ? (double)CompressedSizeBytes / OriginalSizeBytes
        : 1.0;
}
```

## Interface: ICoreMultiProjectorWithCustomSerialization<T>

Implement this interface to define custom serialization for a multi-projector:

```csharp
public interface ICoreMultiProjectorWithCustomSerialization<T> : ICoreMultiProjector
    where T : ICoreMultiProjectorWithCustomSerialization<T>, new()
{
    static abstract SerializationResult Serialize(
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        T payload);

    static abstract T Deserialize(
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        ReadOnlySpan<byte> data);
}
```

## Registration

Register custom serializers using `RegisterProjectorWithCustomSerialization<T>()`:

```csharp
public static DcbDomainTypes GetDomainTypes() =>
    DcbDomainTypes.Simple(types =>
    {
        // Standard registration (uses default JSON + Gzip)
        types.MultiProjectorTypes.RegisterProjector<SimpleProjector>();

        // Custom serialization registration
        types.MultiProjectorTypes.RegisterProjectorWithCustomSerialization<OptimizedProjector>();
    });
```

## Implementation Example with Compression

For large payloads, use Gzip compression:

```csharp
public record CounterProjector(int Count)
    : ICoreMultiProjectorWithCustomSerialization<CounterProjector>
{
    public static string MultiProjectorName => "CounterProjector";
    public static int MultiProjectorVersion => 1;

    public static SerializationResult Serialize(
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        CounterProjector payload)
    {
        if (string.IsNullOrWhiteSpace(safeWindowThreshold))
            throw new ArgumentException("safeWindowThreshold must be supplied");

        var json = JsonSerializer.Serialize(
            new { v = 1, count = payload.Count },
            domainTypes.JsonSerializerOptions);
        var rawBytes = Encoding.UTF8.GetBytes(json);
        var originalSize = rawBytes.LongLength;
        var compressed = GzipCompression.Compress(rawBytes);
        var compressedSize = compressed.LongLength;

        return new SerializationResult(compressed, originalSize, compressedSize);
    }

    public static CounterProjector Deserialize(
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        ReadOnlySpan<byte> data)
    {
        var rawBytes = GzipCompression.Decompress(data.ToArray());
        var json = Encoding.UTF8.GetString(rawBytes);
        var obj = JsonSerializer.Deserialize<JsonObject>(json, domainTypes.JsonSerializerOptions);
        var count = obj?["count"]?.GetValue<int>() ?? 0;
        return new CounterProjector(count);
    }

    // ... Project methods
}
```

## Implementation Example without Compression

For small payloads, skip compression for faster serialization:

```csharp
public record SmallProjector(string Value)
    : ICoreMultiProjectorWithCustomSerialization<SmallProjector>
{
    public static string MultiProjectorName => "SmallProjector";
    public static int MultiProjectorVersion => 1;

    public static SerializationResult Serialize(
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        SmallProjector payload)
    {
        var json = JsonSerializer.Serialize(payload, domainTypes.JsonSerializerOptions);
        var rawBytes = Encoding.UTF8.GetBytes(json);
        var size = rawBytes.LongLength;

        // No compression: OriginalSize = CompressedSize
        return new SerializationResult(rawBytes, size, size);
    }

    public static SmallProjector Deserialize(
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        ReadOnlySpan<byte> data)
    {
        var json = Encoding.UTF8.GetString(data);
        return JsonSerializer.Deserialize<SmallProjector>(json, domainTypes.JsonSerializerOptions)!;
    }
}
```

## Data Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│                  SimpleMultiProjectorTypes.Serialize                │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Custom serializer registered?                                      │
│  ├─ Yes → T.Serialize(domain, threshold, payload)                   │
│  │         → Returns SerializationResult directly                   │
│  │         (Compression fully controlled by custom serializer)      │
│  │                                                                  │
│  └─ No (Fallback)                                                   │
│       1. JSON serialize → rawBytes                                  │
│       2. OriginalSize = rawBytes.Length                             │
│       3. Gzip compress → compressed                                 │
│       4. CompressedSize = compressed.Length                         │
│       5. SerializationResult(compressed, Original, Compressed)      │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│                 SimpleMultiProjectorTypes.Deserialize               │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Custom serializer registered?                                      │
│  ├─ Yes → T.Deserialize(domain, threshold, data)                    │
│  │         → Data passed as-is                                      │
│  │         (Decompression fully controlled by custom serializer)    │
│  │                                                                  │
│  └─ No (Fallback)                                                   │
│       1. Gzip decompress → rawBytes                                 │
│       2. JSON deserialize → payload                                 │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

## Versioning

When changing the serialization format or compression method:

1. Increment `MultiProjectorVersion` in your projector
2. The system will rebuild from events rather than loading old snapshots
3. Old stored states will be automatically replaced with new format

```csharp
public static int MultiProjectorVersion => 2; // Bump when changing format
```

## Offload Threshold

The `CompressedSizeBytes` is used for offload decisions (e.g., moving large states to blob storage).
Ensure your `SerializationResult` reports accurate sizes for proper threshold calculations.

## Migration

When upgrading to a version that changes serialization format:

- **Postgres**: `DELETE FROM "MultiProjectionStates";`
- **Cosmos DB**: Delete documents from MultiProjectionStates container
- **Orleans Grain State**: Automatically rebuilds

The system will rebuild states from events on first access.

## Best Practices

1. **Compression**: Use Gzip for payloads > 1KB, skip for smaller ones
2. **Versioning**: Include a version number in your serialized format
3. **safeWindowThreshold**: Always validate this parameter; it defines serialization scope
4. **Error handling**: Throw exceptions on serialization failures; the system wraps them in `ResultBox`
5. **Testing**: Test both serialization round-trips and version upgrades
