using Sekiban.Pure.Events;
namespace Sekiban.Pure.Orleans.Surrogates;

[RegisterConverter]
public sealed class OrleansEventConverter<TEventPayload> : IConverter<Event<TEventPayload>, OrleansEvent<TEventPayload>>
    where TEventPayload : IEventPayload
{
    private readonly OrleansPartitionKeysConverter _partitionKeysConverter = new();
    private readonly OrleansEventMetadataConverter _metadataConverter = new();

    public Event<TEventPayload> ConvertFromSurrogate(in OrleansEvent<TEventPayload> surrogate) =>
        new(surrogate.Id,
            surrogate.Payload,
            _partitionKeysConverter.ConvertFromSurrogate(surrogate.PartitionKeys),
            surrogate.SortableUniqueId,
            surrogate.Version,
            _metadataConverter.ConvertFromSurrogate(surrogate.Metadata));

    public OrleansEvent<TEventPayload> ConvertToSurrogate(in Event<TEventPayload> value) =>
        new(value.Id,
            value.Payload,
            _partitionKeysConverter.ConvertToSurrogate(value.PartitionKeys),
            value.SortableUniqueId,
            value.Version,
            _metadataConverter.ConvertToSurrogate(value.Metadata));
}