using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Events;
using Sekiban.Pure.Dapr.Protos;

namespace Sekiban.Pure.Dapr.Serialization;

/// <summary>
/// Extended serialization service interface with Protobuf support
/// </summary>
public interface IDaprProtobufSerializationService : IDaprSerializationService
{
    /// <summary>
    /// Serializes an aggregate to a Protobuf envelope
    /// </summary>
    ValueTask<ProtobufAggregateEnvelope> SerializeAggregateToProtobufAsync(IAggregate aggregate);

    /// <summary>
    /// Deserializes an aggregate from a Protobuf envelope
    /// </summary>
    ValueTask<IAggregate?> DeserializeAggregateFromProtobufAsync(ProtobufAggregateEnvelope envelope);

    /// <summary>
    /// Serializes a command to a Protobuf envelope
    /// </summary>
    ValueTask<ProtobufCommandEnvelope> SerializeCommandToProtobufAsync(ICommandWithHandlerSerializable command);

    /// <summary>
    /// Deserializes a command from a Protobuf envelope
    /// </summary>
    ValueTask<ICommandWithHandlerSerializable?> DeserializeCommandFromProtobufAsync(ProtobufCommandEnvelope envelope);

    /// <summary>
    /// Serializes an event to a Protobuf envelope
    /// </summary>
    ValueTask<ProtobufEventEnvelope> SerializeEventToProtobufAsync(IEvent @event, Guid aggregateId, int version, string rootPartitionKey);

    /// <summary>
    /// Deserializes an event from a Protobuf envelope
    /// </summary>
    ValueTask<IEvent?> DeserializeEventFromProtobufAsync(ProtobufEventEnvelope envelope);
}