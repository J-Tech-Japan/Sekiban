using Sekiban.Pure.Documents;
namespace Sekiban.Pure.Orleans.Surrogates;

[RegisterConverter]
public sealed class OrleansPartitionKeysConverter : IConverter<PartitionKeys, OrleansPartitionKeys>
{
    public PartitionKeys ConvertFromSurrogate(in OrleansPartitionKeys surrogate) =>
        new(surrogate.AggregateId, surrogate.Group, surrogate.RootPartitionKey);

    public OrleansPartitionKeys ConvertToSurrogate(in PartitionKeys value) =>
        new(value.AggregateId, value.Group, value.RootPartitionKey);
}