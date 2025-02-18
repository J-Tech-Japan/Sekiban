using Sekiban.Pure.Command.Executor;
namespace Sekiban.Pure.Orleans.Surrogates;

[RegisterConverter]
public sealed class OrleansCommandResponseConverter : IConverter<CommandResponse, OrleansCommandResponse>
{
    private readonly OrleansPartitionKeysConverter _partitionKeysConverter = new();

    public CommandResponse ConvertFromSurrogate(in OrleansCommandResponse surrogate) =>
        new(_partitionKeysConverter.ConvertFromSurrogate(surrogate.PartitionKeys), surrogate.Events, surrogate.Version);

    public OrleansCommandResponse ConvertToSurrogate(in CommandResponse value) =>
        new(_partitionKeysConverter.ConvertToSurrogate(value.PartitionKeys), value.Events, value.Version);
}