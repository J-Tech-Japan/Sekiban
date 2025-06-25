using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Dapr.Protos;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;

namespace Sekiban.Pure.Dapr.Serialization;

/// <summary>
/// Protobuf-based serialization service for Dapr actors
/// </summary>
public class DaprProtobufSerializationService : IDaprProtobufSerializationService
{
    private readonly IDaprTypeRegistry _typeRegistry;
    private readonly DaprSerializationOptions _options;
    private readonly ILogger<DaprProtobufSerializationService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public DaprProtobufSerializationService(
        IDaprTypeRegistry typeRegistry,
        IOptions<DaprSerializationOptions> options,
        ILogger<DaprProtobufSerializationService> logger)
    {
        _typeRegistry = typeRegistry ?? throw new ArgumentNullException(nameof(typeRegistry));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonOptions = _options.JsonSerializerOptions;
    }

    public async ValueTask<byte[]> SerializeAsync<T>(T value)
    {
        if (value == null)
        {
            return Array.Empty<byte>();
        }

        try
        {
            // For generic types, we still use JSON within protobuf
            var json = JsonSerializer.SerializeToUtf8Bytes(value, _jsonOptions);
            
            if (_options.EnableCompression && json.Length > _options.CompressionThreshold)
            {
                return DaprCompressionUtility.Compress(json);
            }

            return json;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize object of type {Type}", typeof(T).Name);
            throw;
        }
    }

    public async ValueTask<T?> DeserializeAsync<T>(byte[] data)
    {
        if (data == null || data.Length == 0)
        {
            return default;
        }

        try
        {
            byte[] json = data;

            if (_options.EnableCompression && IsCompressed(data))
            {
                json = DaprCompressionUtility.Decompress(data);
            }

            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize object of type {Type}", typeof(T).Name);
            throw;
        }
    }

    public async ValueTask<DaprAggregateSurrogate> SerializeAggregateAsync(IAggregate aggregate)
    {
        // This method is for backward compatibility - internally we use protobuf
        var protobufEnvelope = await SerializeAggregateToProtobufAsync(aggregate);
        
        return new DaprAggregateSurrogate
        {
            CompressedPayload = protobufEnvelope.PayloadJson.ToByteArray(),
            PayloadTypeName = protobufEnvelope.PayloadType,
            Version = protobufEnvelope.Version,
            AggregateId = protobufEnvelope.AggregateId,
            RootPartitionKey = protobufEnvelope.RootPartitionKey,
            LastEventId = protobufEnvelope.LastEventId,
            IsCompressed = protobufEnvelope.IsCompressed,
            Metadata = new Dictionary<string, string>(protobufEnvelope.Metadata)
        };
    }

    public async ValueTask<IAggregate?> DeserializeAggregateAsync(DaprAggregateSurrogate surrogate)
    {
        // Convert from surrogate to protobuf for processing
        var protobufEnvelope = new ProtobufAggregateEnvelope
        {
            PayloadJson = ByteString.CopyFrom(surrogate.CompressedPayload),
            PayloadType = surrogate.PayloadTypeName,
            Version = surrogate.Version,
            AggregateId = surrogate.AggregateId,
            RootPartitionKey = surrogate.RootPartitionKey,
            LastEventId = surrogate.LastEventId,
            IsCompressed = surrogate.IsCompressed
        };
        
        foreach (var kvp in surrogate.Metadata)
        {
            protobufEnvelope.Metadata[kvp.Key] = kvp.Value;
        }

        return await DeserializeAggregateFromProtobufAsync(protobufEnvelope);
    }

    // New Protobuf-specific methods
    public async ValueTask<ProtobufAggregateEnvelope> SerializeAggregateToProtobufAsync(IAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        try
        {
            var payload = aggregate.GetPayload();
            var payloadType = payload.GetType();
            var json = JsonSerializer.SerializeToUtf8Bytes(payload, payloadType, _jsonOptions);

            byte[] finalData;
            bool isCompressed = false;

            if (_options.EnableCompression && json.Length > _options.CompressionThreshold)
            {
                finalData = DaprCompressionUtility.Compress(json);
                isCompressed = true;
            }
            else
            {
                finalData = json;
            }

            var typeAlias = _options.EnableTypeAliases 
                ? _typeRegistry.GetTypeAlias(payloadType) 
                : payloadType.AssemblyQualifiedName ?? payloadType.FullName ?? payloadType.Name;

            var envelope = new ProtobufAggregateEnvelope
            {
                PayloadJson = ByteString.CopyFrom(finalData),
                PayloadType = typeAlias,
                Version = aggregate.Version,
                AggregateId = aggregate.PartitionKeys.AggregateId.ToString(),
                RootPartitionKey = aggregate.PartitionKeys.RootPartitionKey,
                LastEventId = aggregate.LastSortableUniqueId,
                IsCompressed = isCompressed
            };
            
            envelope.Metadata["SerializedAt"] = DateTime.UtcNow.ToString("O");
            envelope.Metadata["SerializerVersion"] = "2.0-protobuf";
            
            return envelope;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize aggregate {AggregateId} to protobuf", aggregate.PartitionKeys.AggregateId);
            throw;
        }
    }

    public async ValueTask<IAggregate?> DeserializeAggregateFromProtobufAsync(ProtobufAggregateEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.PayloadJson == null || envelope.PayloadJson.IsEmpty)
        {
            return null;
        }

        try
        {
            Type? payloadType = null;

            if (_options.EnableTypeAliases)
            {
                payloadType = _typeRegistry.ResolveType(envelope.PayloadType);
            }

            if (payloadType == null)
            {
                payloadType = Type.GetType(envelope.PayloadType);
            }

            if (payloadType == null)
            {
                _logger.LogError("Cannot resolve type {TypeName}", envelope.PayloadType);
                throw new InvalidOperationException($"Cannot resolve type: {envelope.PayloadType}");
            }

            byte[] json = envelope.IsCompressed
                ? DaprCompressionUtility.Decompress(envelope.PayloadJson.ToByteArray())
                : envelope.PayloadJson.ToByteArray();

            var payload = JsonSerializer.Deserialize(json, payloadType, _jsonOptions);
            
            if (payload == null)
            {
                return null;
            }

            // Parse AggregateId as Guid
            if (!Guid.TryParse(envelope.AggregateId, out var aggregateId))
            {
                throw new InvalidOperationException($"Invalid aggregate ID format: {envelope.AggregateId}");
            }

            // Create aggregate instance
            var aggregate = new Aggregate(
                payload as IAggregatePayload ?? throw new InvalidOperationException("Payload must implement IAggregatePayload"),
                new PartitionKeys(aggregateId, string.Empty, envelope.RootPartitionKey),
                envelope.Version,
                envelope.LastEventId,
                "1", // ProjectorVersion - TODO: need to handle this properly
                payloadType.Name,
                payloadType.AssemblyQualifiedName ?? payloadType.FullName ?? payloadType.Name);

            return aggregate;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize aggregate from protobuf envelope");
            throw;
        }
    }

    public async ValueTask<DaprCommandEnvelope> SerializeCommandAsync(ICommandWithHandlerSerializable command)
    {
        // For backward compatibility
        var protobufEnvelope = await SerializeCommandToProtobufAsync(command);
        
        return new DaprCommandEnvelope
        {
            CommandData = protobufEnvelope.CommandJson.ToByteArray(),
            CommandType = protobufEnvelope.CommandType,
            IsCompressed = protobufEnvelope.IsCompressed,
            Headers = new Dictionary<string, string>(protobufEnvelope.Headers),
            CorrelationId = protobufEnvelope.CorrelationId
        };
    }

    public async ValueTask<ICommandWithHandlerSerializable?> DeserializeCommandAsync(DaprCommandEnvelope envelope)
    {
        // Convert to protobuf for processing
        var protobufEnvelope = new ProtobufCommandEnvelope
        {
            CommandJson = ByteString.CopyFrom(envelope.CommandData),
            CommandType = envelope.CommandType,
            IsCompressed = envelope.IsCompressed,
            CorrelationId = envelope.CorrelationId
        };
        
        foreach (var kvp in envelope.Headers)
        {
            protobufEnvelope.Headers[kvp.Key] = kvp.Value;
        }

        return await DeserializeCommandFromProtobufAsync(protobufEnvelope);
    }

    // New Protobuf command methods
    public async ValueTask<ProtobufCommandEnvelope> SerializeCommandToProtobufAsync(ICommandWithHandlerSerializable command)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var commandType = command.GetType();
            var json = JsonSerializer.SerializeToUtf8Bytes(command, commandType, _jsonOptions);

            byte[] finalData;
            bool isCompressed = false;

            if (_options.EnableCompression && json.Length > _options.CompressionThreshold)
            {
                finalData = DaprCompressionUtility.Compress(json);
                isCompressed = true;
            }
            else
            {
                finalData = json;
            }

            var typeAlias = _options.EnableTypeAliases
                ? _typeRegistry.GetTypeAlias(commandType)
                : commandType.AssemblyQualifiedName ?? commandType.FullName ?? commandType.Name;

            var envelope = new ProtobufCommandEnvelope
            {
                CommandJson = ByteString.CopyFrom(finalData),
                CommandType = typeAlias,
                IsCompressed = isCompressed,
                CorrelationId = Guid.NewGuid().ToString()
            };
            
            envelope.Headers["CommandTypeFull"] = commandType.FullName ?? commandType.Name;
            
            return envelope;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize command of type {Type} to protobuf", command.GetType().Name);
            throw;
        }
    }

    public async ValueTask<ICommandWithHandlerSerializable?> DeserializeCommandFromProtobufAsync(ProtobufCommandEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.CommandJson == null || envelope.CommandJson.IsEmpty)
        {
            return null;
        }

        try
        {
            Type? commandType = null;

            if (_options.EnableTypeAliases)
            {
                commandType = _typeRegistry.ResolveType(envelope.CommandType);
            }

            if (commandType == null)
            {
                commandType = Type.GetType(envelope.CommandType);
            }

            if (commandType == null)
            {
                _logger.LogError("Cannot resolve command type {TypeName}", envelope.CommandType);
                throw new InvalidOperationException($"Cannot resolve command type: {envelope.CommandType}");
            }

            byte[] json = envelope.IsCompressed
                ? DaprCompressionUtility.Decompress(envelope.CommandJson.ToByteArray())
                : envelope.CommandJson.ToByteArray();

            var command = JsonSerializer.Deserialize(json, commandType, _jsonOptions) as ICommandWithHandlerSerializable;
            return command;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize command from protobuf envelope");
            throw;
        }
    }

    public async ValueTask<DaprEventEnvelope> SerializeEventAsync(IEvent @event, Guid aggregateId, int version, string rootPartitionKey)
    {
        // For backward compatibility
        var protobufEnvelope = await SerializeEventToProtobufAsync(@event, aggregateId, version, rootPartitionKey);
        
        return new DaprEventEnvelope
        {
            EventId = Guid.Parse(protobufEnvelope.EventId),
            EventData = protobufEnvelope.EventJson.ToByteArray(),
            EventType = protobufEnvelope.EventType,
            AggregateId = Guid.Parse(protobufEnvelope.AggregateId),
            Version = protobufEnvelope.Version,
            Timestamp = protobufEnvelope.Timestamp.ToDateTime(),
            RootPartitionKey = protobufEnvelope.RootPartitionKey,
            IsCompressed = protobufEnvelope.IsCompressed,
            SortableUniqueId = protobufEnvelope.SortableUniqueId,
            Metadata = new Dictionary<string, string>(protobufEnvelope.Metadata)
        };
    }

    public async ValueTask<IEvent?> DeserializeEventAsync(DaprEventEnvelope envelope)
    {
        // Convert to protobuf for processing
        var protobufEnvelope = new ProtobufEventEnvelope
        {
            EventId = envelope.EventId.ToString(),
            EventJson = ByteString.CopyFrom(envelope.EventData),
            EventType = envelope.EventType,
            AggregateId = envelope.AggregateId.ToString(),
            Version = envelope.Version,
            Timestamp = Timestamp.FromDateTime(envelope.Timestamp.ToUniversalTime()),
            RootPartitionKey = envelope.RootPartitionKey,
            IsCompressed = envelope.IsCompressed,
            SortableUniqueId = envelope.SortableUniqueId
        };
        
        foreach (var kvp in envelope.Metadata)
        {
            protobufEnvelope.Metadata[kvp.Key] = kvp.Value;
        }

        return await DeserializeEventFromProtobufAsync(protobufEnvelope);
    }

    // New Protobuf event methods
    public async ValueTask<ProtobufEventEnvelope> SerializeEventToProtobufAsync(IEvent @event, Guid aggregateId, int version, string rootPartitionKey)
    {
        ArgumentNullException.ThrowIfNull(@event);

        try
        {
            var eventType = @event.GetType();
            var json = JsonSerializer.SerializeToUtf8Bytes(@event, eventType, _jsonOptions);

            byte[] finalData;
            bool isCompressed = false;

            if (_options.EnableCompression && json.Length > _options.CompressionThreshold)
            {
                finalData = DaprCompressionUtility.Compress(json);
                isCompressed = true;
            }
            else
            {
                finalData = json;
            }

            var typeAlias = _options.EnableTypeAliases
                ? _typeRegistry.GetTypeAlias(eventType)
                : eventType.AssemblyQualifiedName ?? eventType.FullName ?? eventType.Name;

            var envelope = new ProtobufEventEnvelope
            {
                EventId = Guid.NewGuid().ToString(),
                EventJson = ByteString.CopyFrom(finalData),
                EventType = typeAlias,
                AggregateId = aggregateId.ToString(),
                Version = version,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                RootPartitionKey = rootPartitionKey,
                IsCompressed = isCompressed,
                SortableUniqueId = @event.GetSortableUniqueId()
            };
            
            envelope.Metadata["EventTypeFull"] = eventType.FullName ?? eventType.Name;
            
            return envelope;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize event of type {Type} to protobuf", @event.GetType().Name);
            throw;
        }
    }

    public async ValueTask<IEvent?> DeserializeEventFromProtobufAsync(ProtobufEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.EventJson == null || envelope.EventJson.IsEmpty)
        {
            return null;
        }

        try
        {
            Type? eventType = null;

            if (_options.EnableTypeAliases)
            {
                eventType = _typeRegistry.ResolveType(envelope.EventType);
            }

            if (eventType == null)
            {
                eventType = Type.GetType(envelope.EventType);
            }

            if (eventType == null)
            {
                _logger.LogError("Cannot resolve event type {TypeName}", envelope.EventType);
                throw new InvalidOperationException($"Cannot resolve event type: {envelope.EventType}");
            }

            byte[] json = envelope.IsCompressed
                ? DaprCompressionUtility.Decompress(envelope.EventJson.ToByteArray())
                : envelope.EventJson.ToByteArray();

            var @event = JsonSerializer.Deserialize(json, eventType, _jsonOptions) as IEvent;
            return @event;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize event from protobuf envelope");
            throw;
        }
    }

    private static bool IsCompressed(byte[] data)
    {
        // Check for GZip magic number
        return data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b;
    }
}