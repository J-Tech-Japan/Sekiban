using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Dapr.Actors;
using Sekiban.Pure.Dapr.Protos;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using System.Text.Json;

namespace Sekiban.Pure.Dapr.Serialization;

/// <summary>
/// Service for converting between domain objects and envelopes with Protobuf payloads
/// </summary>
public interface IEnvelopeProtobufService
{
    /// <summary>
    /// Creates a CommandEnvelope from a command
    /// </summary>
    Task<CommandEnvelope> CreateCommandEnvelope(ICommandWithHandlerSerializable command, PartitionKeys partitionKeys, Dictionary<string, string>? metadata = null);

    /// <summary>
    /// Extracts a command from a CommandEnvelope
    /// </summary>
    Task<ICommandWithHandlerSerializable> ExtractCommand(CommandEnvelope envelope);

    /// <summary>
    /// Creates EventEnvelopes from events
    /// </summary>
    Task<List<EventEnvelope>> CreateEventEnvelopes(IEnumerable<IEvent> events, PartitionKeys partitionKeys, string correlationId);

    /// <summary>
    /// Extracts events from EventEnvelopes
    /// </summary>
    Task<List<IEvent>> ExtractEvents(IEnumerable<EventEnvelope> envelopes);

    /// <summary>
    /// Converts a CommandResponse with events to use Protobuf payloads
    /// </summary>
    Task<CommandResponse> ConvertToProtobufResponse(Sekiban.Pure.Command.Executor.CommandResponse response, PartitionKeys partitionKeys);
}

/// <summary>
/// Default implementation of IEnvelopeProtobufService
/// </summary>
public class EnvelopeProtobufService : IEnvelopeProtobufService
{
    private readonly IDaprProtobufSerializationService _protobufSerialization;
    private readonly IProtobufTypeMapper _typeMapper;
    private readonly ILogger<EnvelopeProtobufService> _logger;

    public EnvelopeProtobufService(
        IDaprProtobufSerializationService protobufSerialization,
        IProtobufTypeMapper typeMapper,
        ILogger<EnvelopeProtobufService> logger)
    {
        _protobufSerialization = protobufSerialization ?? throw new ArgumentNullException(nameof(protobufSerialization));
        _typeMapper = typeMapper ?? throw new ArgumentNullException(nameof(typeMapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CommandEnvelope> CreateCommandEnvelope(
        ICommandWithHandlerSerializable command, 
        PartitionKeys partitionKeys,
        Dictionary<string, string>? metadata = null)
    {
        try
        {
            // Create Protobuf command envelope
            var protobufEnvelope = await _protobufSerialization.SerializeCommandToProtobufAsync(command);
            
            // Extract the payload for the envelope
            var envelope = new CommandEnvelope(
                commandType: command.GetType().FullName ?? command.GetType().Name,
                commandPayload: protobufEnvelope.CommandJson.ToByteArray(),
                aggregateId: partitionKeys.AggregateId.ToString(),
                partitionId: partitionKeys.AggregateId, // Using AggregateId as PartitionId
                rootPartitionKey: partitionKeys.RootPartitionKey,
                metadata: metadata ?? new Dictionary<string, string>(),
                correlationId: protobufEnvelope.CorrelationId);

            _logger.LogDebug("Created CommandEnvelope for {CommandType} targeting aggregate {AggregateId}",
                envelope.CommandType, envelope.AggregateId);

            return envelope;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create CommandEnvelope for command type {CommandType}",
                command.GetType().Name);
            throw;
        }
    }

    public async Task<ICommandWithHandlerSerializable> ExtractCommand(CommandEnvelope envelope)
    {
        try
        {
            // Create ProtobufCommandEnvelope from the envelope
            var protobufEnvelope = new ProtobufCommandEnvelope
            {
                CommandJson = ByteString.CopyFrom(envelope.CommandPayload),
                CommandType = envelope.CommandType,
                IsCompressed = false, // The payload is already the inner JSON
                CorrelationId = envelope.CorrelationId,
                PartitionKey = envelope.AggregateId
            };

            foreach (var kvp in envelope.Metadata)
            {
                protobufEnvelope.Headers[kvp.Key] = kvp.Value;
            }

            // Deserialize using the Protobuf service
            var command = await _protobufSerialization.DeserializeCommandFromProtobufAsync(protobufEnvelope);
            
            if (command == null)
            {
                throw new InvalidOperationException($"Failed to deserialize command from envelope with type {envelope.CommandType}");
            }

            _logger.LogDebug("Extracted command of type {CommandType} from envelope", envelope.CommandType);

            return command;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract command from envelope with type {CommandType}", envelope.CommandType);
            throw;
        }
    }

    public async Task<List<EventEnvelope>> CreateEventEnvelopes(
        IEnumerable<IEvent> events, 
        PartitionKeys partitionKeys,
        string correlationId)
    {
        var envelopes = new List<EventEnvelope>();

        foreach (var @event in events)
        {
            try
            {
                // Create Protobuf event envelope
                var protobufEnvelope = await _protobufSerialization.SerializeEventToProtobufAsync(
                    @event,
                    partitionKeys.AggregateId,
                    @event.Version,
                    partitionKeys.RootPartitionKey);

                // Create the envelope
                var envelope = new EventEnvelope(
                    eventType: @event.GetType().FullName ?? @event.GetType().Name,
                    eventPayload: protobufEnvelope.EventJson.ToByteArray(),
                    aggregateId: partitionKeys.AggregateId.ToString(),
                    partitionId: partitionKeys.AggregateId,
                    rootPartitionKey: partitionKeys.RootPartitionKey,
                    version: @event.Version,
                    sortableUniqueId: @event.GetSortableUniqueId(),
                    metadata: new Dictionary<string, string>(protobufEnvelope.Metadata),
                    correlationId: correlationId);

                envelopes.Add(envelope);

                _logger.LogDebug("Created EventEnvelope for {EventType} version {Version}",
                    envelope.EventType, envelope.Version);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create EventEnvelope for event type {EventType}",
                    @event.GetType().Name);
                throw;
            }
        }

        return envelopes;
    }

    public async Task<List<IEvent>> ExtractEvents(IEnumerable<EventEnvelope> envelopes)
    {
        var events = new List<IEvent>();

        foreach (var envelope in envelopes)
        {
            try
            {
                // Create ProtobufEventEnvelope from the envelope
                var protobufEnvelope = new ProtobufEventEnvelope
                {
                    EventId = Guid.NewGuid().ToString(),
                    EventJson = ByteString.CopyFrom(envelope.EventPayload),
                    EventType = envelope.EventType,
                    AggregateId = envelope.AggregateId,
                    Version = envelope.Version,
                    Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(envelope.Timestamp.ToUniversalTime()),
                    RootPartitionKey = envelope.RootPartitionKey,
                    IsCompressed = false, // The payload is already the inner JSON
                    SortableUniqueId = envelope.SortableUniqueId
                };

                foreach (var kvp in envelope.Metadata)
                {
                    protobufEnvelope.Metadata[kvp.Key] = kvp.Value;
                }

                // Deserialize using the Protobuf service
                var @event = await _protobufSerialization.DeserializeEventFromProtobufAsync(protobufEnvelope);
                
                if (@event != null)
                {
                    events.Add(@event);
                    _logger.LogDebug("Extracted event of type {EventType} version {Version}",
                        envelope.EventType, envelope.Version);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract event from envelope with type {EventType}",
                    envelope.EventType);
                throw;
            }
        }

        return events;
    }

    public async Task<CommandResponse> ConvertToProtobufResponse(
        Sekiban.Pure.Command.Executor.CommandResponse response,
        PartitionKeys partitionKeys)
    {
        try
        {
            if (!response.IsSuccess)
            {
                // Create error response
                var errorData = new
                {
                    Message = response.ErrorMessage ?? "Command execution failed",
                    IsSuccess = false
                };
                
                return CommandResponse.Failure(
                    JsonSerializer.Serialize(errorData),
                    response.Metadata);
            }

            // Serialize events to Protobuf
            var eventPayloads = new List<byte[]>();
            var eventTypes = new List<string>();

            foreach (var @event in response.Events)
            {
                var protobufEnvelope = await _protobufSerialization.SerializeEventToProtobufAsync(
                    @event,
                    partitionKeys.AggregateId,
                    @event.Version,
                    partitionKeys.RootPartitionKey);

                eventPayloads.Add(protobufEnvelope.EventJson.ToByteArray());
                eventTypes.Add(@event.GetType().FullName ?? @event.GetType().Name);
            }

            // Serialize aggregate state if present
            byte[]? aggregateStatePayload = null;
            string? aggregateStateType = null;

            if (response.AggregateState != null)
            {
                var protobufAggregate = await _protobufSerialization.SerializeAggregateToProtobufAsync(response.AggregateState);
                aggregateStatePayload = protobufAggregate.PayloadJson.ToByteArray();
                aggregateStateType = protobufAggregate.PayloadType;
            }

            return CommandResponse.Success(
                eventPayloads,
                eventTypes,
                response.Version,
                aggregateStatePayload,
                aggregateStateType,
                response.Metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert CommandResponse to Protobuf format");
            throw;
        }
    }
}