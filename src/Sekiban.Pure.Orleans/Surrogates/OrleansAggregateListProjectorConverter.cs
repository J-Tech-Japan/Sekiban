using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Projectors;
using System.Collections.Immutable;
namespace Sekiban.Pure.Orleans.Surrogates;

[RegisterConverter]
public sealed class OrleansAggregateListProjectorConverter<TAggregateProjector> : 
    IConverter<AggregateListProjector<TAggregateProjector>, OrleansAggregateListProjector<TAggregateProjector>>
    where TAggregateProjector : IAggregateProjector, new()
{
    private readonly OrleansPartitionKeysConverter _partitionKeysConverter = new();
    private readonly OrleansAggregateConverter _aggregateConverter = new();

    public AggregateListProjector<TAggregateProjector> ConvertFromSurrogate(
        in OrleansAggregateListProjector<TAggregateProjector> surrogate)
    {
        var convertedDict = surrogate.Aggregates
            .Select(kvp => new KeyValuePair<PartitionKeys, Aggregate>(
                _partitionKeysConverter.ConvertFromSurrogate(kvp.Key),
                _aggregateConverter.ConvertFromSurrogate(kvp.Value)))
            .ToImmutableDictionary();
        return new AggregateListProjector<TAggregateProjector>(convertedDict);
    }

    public OrleansAggregateListProjector<TAggregateProjector> ConvertToSurrogate(
        in AggregateListProjector<TAggregateProjector> value)
    {
        var convertedDict = value.Aggregates
            .Select(kvp => new KeyValuePair<OrleansPartitionKeys, OrleansAggregate>(
                _partitionKeysConverter.ConvertToSurrogate(kvp.Key),
                _aggregateConverter.ConvertToSurrogate(kvp.Value)))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        return new OrleansAggregateListProjector<TAggregateProjector>(convertedDict);
    }
}