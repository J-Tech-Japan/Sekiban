using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Events;

namespace Sekiban.Pure.Dapr.Serialization;

/// <summary>
/// Service for handling serialization and deserialization in Dapr
/// </summary>
public interface IDaprSerializationService
{
    /// <summary>
    /// Serializes an object to compressed byte array
    /// </summary>
    ValueTask<byte[]> SerializeAsync<T>(T value);

    /// <summary>
    /// Deserializes an object from compressed byte array
    /// </summary>
    ValueTask<T?> DeserializeAsync<T>(byte[] data);

    /// <summary>
    /// Serializes an aggregate to a surrogate
    /// </summary>
    ValueTask<DaprAggregateSurrogate> SerializeAggregateAsync(IAggregate aggregate);

    /// <summary>
    /// Deserializes an aggregate from a surrogate
    /// </summary>
    ValueTask<IAggregate?> DeserializeAggregateAsync(DaprAggregateSurrogate surrogate);

    /// <summary>
    /// Serializes a command to an envelope
    /// </summary>
    ValueTask<DaprCommandEnvelope> SerializeCommandAsync(ICommandWithHandlerSerializable command);

    /// <summary>
    /// Deserializes a command from an envelope
    /// </summary>
    ValueTask<ICommandWithHandlerSerializable?> DeserializeCommandAsync(DaprCommandEnvelope envelope);

    /// <summary>
    /// Serializes an event to an envelope
    /// </summary>
    ValueTask<DaprEventEnvelope> SerializeEventAsync(IEvent @event, Guid aggregateId, int version, string rootPartitionKey);

    /// <summary>
    /// Deserializes an event from an envelope
    /// </summary>
    ValueTask<IEvent?> DeserializeEventAsync(DaprEventEnvelope envelope);
}