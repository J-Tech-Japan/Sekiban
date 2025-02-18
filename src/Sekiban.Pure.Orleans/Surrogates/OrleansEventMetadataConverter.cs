using Sekiban.Pure.Events;
namespace Sekiban.Pure.Orleans.Surrogates;

[RegisterConverter]
public sealed class OrleansEventMetadataConverter : IConverter<EventMetadata, OrleansEventMetadata>
{
    public EventMetadata ConvertFromSurrogate(in OrleansEventMetadata surrogate) =>
        new(surrogate.CausationId, surrogate.CorrelationId, surrogate.ExecutedUser);

    public OrleansEventMetadata ConvertToSurrogate(in EventMetadata value) =>
        new(value.CausationId, value.CorrelationId, value.ExecutedUser);
}