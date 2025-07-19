using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Pure;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Events;
using Sekiban.Pure.Documents;

namespace Sekiban.Pure.Dapr.Serialization;

/// <summary>
/// Default implementation of Dapr serialization service
/// </summary>
public class DaprSerializationService : IDaprSerializationService
{
    private readonly IDaprTypeRegistry _typeRegistry;
    private readonly DaprSerializationOptions _options;
    private readonly ILogger<DaprSerializationService> _logger;
    private readonly SekibanDomainTypes _domainTypes;

    public DaprSerializationService(
        IDaprTypeRegistry typeRegistry,
        IOptions<DaprSerializationOptions> options,
        ILogger<DaprSerializationService> logger,
        SekibanDomainTypes domainTypes)
    {
        _typeRegistry = typeRegistry ?? throw new ArgumentNullException(nameof(typeRegistry));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
    }

    public async ValueTask<byte[]> SerializeAsync<T>(T value)
    {
        if (value == null)
        {
            return Array.Empty<byte>();
        }

        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(value, _options.JsonSerializerOptions);

            if (_options.EnableCompression && json.Length > _options.CompressionThreshold)
            {
                return DaprCompressionUtility.Compress(json);
            }
            await Task.CompletedTask;
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

            // Try to decompress if the data appears to be compressed
            if (_options.EnableCompression && IsCompressed(data))
            {
                json = DaprCompressionUtility.Decompress(data);
            }
            await Task.CompletedTask;
            return JsonSerializer.Deserialize<T>(json, _options.JsonSerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize object of type {Type}", typeof(T).Name);
            throw;
        }
    }

    public async ValueTask<DaprAggregateSurrogate> SerializeAggregateAsync(IAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        try
        {
            var payload = aggregate.GetPayload();
            var payloadType = payload.GetType();
            var json = JsonSerializer.SerializeToUtf8Bytes(payload, payloadType, _options.JsonSerializerOptions);

            byte[] compressedPayload;
            bool isCompressed = false;

            if (_options.EnableCompression && json.Length > _options.CompressionThreshold)
            {
                compressedPayload = DaprCompressionUtility.Compress(json);
                isCompressed = true;
            }
            else
            {
                compressedPayload = json;
            }

            var typeAlias = _options.EnableTypeAliases 
                ? _typeRegistry.GetTypeAlias(payloadType) 
                : payloadType.AssemblyQualifiedName ?? payloadType.FullName ?? payloadType.Name;
            await Task.CompletedTask;
            return new DaprAggregateSurrogate
            {
                CompressedPayload = compressedPayload,
                PayloadTypeName = typeAlias,
                Version = aggregate.Version,
                AggregateId = aggregate.PartitionKeys.AggregateId,
                RootPartitionKey = aggregate.PartitionKeys.RootPartitionKey,
                LastEventId = aggregate.LastSortableUniqueId,
                IsCompressed = isCompressed,
                Metadata = new Dictionary<string, string>
                {
                    ["SerializedAt"] = DateTime.UtcNow.ToString("O"),
                    ["SerializerVersion"] = "1.0"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize aggregate {AggregateId}", aggregate.PartitionKeys.AggregateId);
            throw;
        }
    }

    public async ValueTask<IAggregate?> DeserializeAggregateAsync(DaprAggregateSurrogate surrogate)
    {
        ArgumentNullException.ThrowIfNull(surrogate);

        if (surrogate.CompressedPayload == null || surrogate.CompressedPayload.Length == 0)
        {
            return null;
        }

        try
        {
            Type? payloadType = null;

            if (_options.EnableTypeAliases)
            {
                payloadType = _typeRegistry.ResolveType(surrogate.PayloadTypeName);
            }

            if (payloadType == null)
            {
                payloadType = Type.GetType(surrogate.PayloadTypeName);
            }

            // If type resolution failed and it looks like a simple type name without assembly info,
            // try to find it in known assemblies
            if (payloadType == null && !surrogate.PayloadTypeName.Contains(','))
            {
                // Try to find the type in Sekiban.Pure assembly
                var sekibanAssembly = typeof(EmptyAggregatePayload).Assembly;
                payloadType = sekibanAssembly.GetType(surrogate.PayloadTypeName);
                
                // If not found, try with namespace
                if (payloadType == null)
                {
                    payloadType = sekibanAssembly.GetType($"Sekiban.Pure.Aggregates.{surrogate.PayloadTypeName}");
                }
                
                // If still not found, try to search all loaded assemblies for the type
                if (payloadType == null)
                {
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        payloadType = assembly.GetType(surrogate.PayloadTypeName);
                        if (payloadType != null)
                        {
                            break;
                        }
                    }
                }
            }

            if (payloadType == null)
            {
                _logger.LogError("Cannot resolve type {TypeName}", surrogate.PayloadTypeName);
                throw new InvalidOperationException($"Cannot resolve type: {surrogate.PayloadTypeName}");
            }

            byte[] json = surrogate.IsCompressed
                ? DaprCompressionUtility.Decompress(surrogate.CompressedPayload)
                : surrogate.CompressedPayload;

            var payload = JsonSerializer.Deserialize(json, payloadType, _options.JsonSerializerOptions);
            
            if (payload == null)
            {
                return null;
            }

            // Create aggregate instance
            var aggregate = new Aggregate(
                payload as IAggregatePayload ?? throw new InvalidOperationException("Payload must implement IAggregatePayload"),
                new PartitionKeys(surrogate.AggregateId, string.Empty, surrogate.RootPartitionKey),
                surrogate.Version,
                surrogate.LastEventId ?? string.Empty, // LastSortableUniqueId
                "1", // ProjectorVersion - TODO: need to handle this properly
                payloadType.Name, // ProjectorTypeName  
                payloadType.AssemblyQualifiedName ?? payloadType.FullName ?? payloadType.Name); // PayloadTypeName
            await Task.CompletedTask;
            return aggregate;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize aggregate from surrogate");
            throw;
        }
    }

    public async ValueTask<DaprCommandEnvelope> SerializeCommandAsync(ICommandWithHandlerSerializable command)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var commandType = command.GetType();
            var json = JsonSerializer.SerializeToUtf8Bytes(command, commandType, _options.JsonSerializerOptions);

            byte[] commandData;
            bool isCompressed = false;

            if (_options.EnableCompression && json.Length > _options.CompressionThreshold)
            {
                commandData = DaprCompressionUtility.Compress(json);
                isCompressed = true;
            }
            else
            {
                commandData = json;
            }

            var typeAlias = _options.EnableTypeAliases
                ? _typeRegistry.GetTypeAlias(commandType)
                : commandType.AssemblyQualifiedName ?? commandType.FullName ?? commandType.Name;
            await Task.CompletedTask;
            return new DaprCommandEnvelope
            {
                CommandData = commandData,
                CommandType = typeAlias,
                IsCompressed = isCompressed,
                Headers = new Dictionary<string, string>
                {
                    ["CommandTypeFull"] = commandType.FullName ?? commandType.Name
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize command of type {Type}", command.GetType().Name);
            throw;
        }
    }

    public async ValueTask<ICommandWithHandlerSerializable?> DeserializeCommandAsync(DaprCommandEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.CommandData == null || envelope.CommandData.Length == 0)
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
                ? DaprCompressionUtility.Decompress(envelope.CommandData)
                : envelope.CommandData;
            await Task.CompletedTask;
            var command = JsonSerializer.Deserialize(json, commandType, _options.JsonSerializerOptions) as ICommandWithHandlerSerializable;
            return command;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize command from envelope");
            throw;
        }
    }

    public async ValueTask<DaprEventEnvelope> SerializeEventAsync(IEvent @event, Guid aggregateId, int version, string rootPartitionKey)
    {
        ArgumentNullException.ThrowIfNull(@event);

        try
        {
            var eventType = @event.GetType();
            var json = JsonSerializer.SerializeToUtf8Bytes(@event, eventType, _options.JsonSerializerOptions);

            byte[] eventData;
            bool isCompressed = false;

            if (_options.EnableCompression && json.Length > _options.CompressionThreshold)
            {
                eventData = DaprCompressionUtility.Compress(json);
                isCompressed = true;
            }
            else
            {
                eventData = json;
            }

            var typeAlias = _options.EnableTypeAliases
                ? _typeRegistry.GetTypeAlias(eventType)
                : eventType.AssemblyQualifiedName ?? eventType.FullName ?? eventType.Name;
            await Task.CompletedTask;
            return new DaprEventEnvelope
            {
                EventId = Guid.NewGuid(),
                EventData = eventData,
                EventType = typeAlias,
                AggregateId = aggregateId,
                Version = version,
                Timestamp = DateTime.UtcNow,
                RootPartitionKey = rootPartitionKey,
                IsCompressed = isCompressed,
                SortableUniqueId = @event.GetSortableUniqueId(),
                Metadata = new Dictionary<string, string>
                {
                    ["EventTypeFull"] = eventType.FullName ?? eventType.Name
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize event of type {Type}", @event.GetType().Name);
            throw;
        }
    }

    public async ValueTask<IEvent?> DeserializeEventAsync(DaprEventEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.EventData == null || envelope.EventData.Length == 0)
        {
            return null;
        }

        try
        {
            byte[] json = envelope.IsCompressed
                ? DaprCompressionUtility.Decompress(envelope.EventData)
                : envelope.EventData;

            // Parse the JSON to JsonNode for EventDocumentCommon
            var jsonNode = JsonSerializer.Deserialize<JsonNode>(json, _options.JsonSerializerOptions);
            if (jsonNode == null)
            {
                return null;
            }

            // Create EventDocumentCommon with the payload data
            var eventDocumentCommon = new EventDocumentCommon(
                envelope.EventId,
                jsonNode, // The payload as JsonNode
                envelope.SortableUniqueId,
                envelope.Version,
                envelope.AggregateId,
                string.Empty, // AggregateGroup - not included in DaprEventEnvelope
                envelope.RootPartitionKey,
                envelope.EventType,
                envelope.Timestamp,
                string.Empty, // PartitionKey - not included in DaprEventEnvelope
                new EventMetadata(
                    envelope.Metadata.GetValueOrDefault("CausationId", string.Empty),
                    envelope.Metadata.GetValueOrDefault("CorrelationId", string.Empty),
                    envelope.Metadata.GetValueOrDefault("ExecutedUser", string.Empty)
                )
            );

            // Use IEventTypes.DeserializeToTyped to properly reconstruct the event
            var eventResult = _domainTypes.EventTypes.DeserializeToTyped(eventDocumentCommon, _options.JsonSerializerOptions);
            
            if (!eventResult.IsSuccess)
            {
                _logger.LogError("Failed to deserialize event: {Error}", eventResult.GetException().Message);
                throw eventResult.GetException();
            }
            await Task.CompletedTask;
            return eventResult.GetValue();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize event from envelope");
            throw;
        }
    }

    private static bool IsCompressed(byte[] data)
    {
        // Check for GZip magic number
        return data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b;
    }
}