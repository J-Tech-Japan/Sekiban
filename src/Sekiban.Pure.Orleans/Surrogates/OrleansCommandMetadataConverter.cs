using Sekiban.Pure.Command.Handlers;
namespace Sekiban.Pure.Orleans.Surrogates;

[RegisterConverter]
public sealed class OrleansCommandMetadataConverter : IConverter<CommandMetadata, OrleansCommandMetadata>
{
    public OrleansCommandMetadata ConvertToSurrogate(in CommandMetadata value) =>
        new(value.CommandId, value.CausationId, value.CorrelationId, value.ExecutedUser);

    public CommandMetadata ConvertFromSurrogate(in OrleansCommandMetadata surrogate) =>
        new(surrogate.CommandId, surrogate.CausationId, surrogate.CorrelationId, surrogate.ExecutedUser);
}