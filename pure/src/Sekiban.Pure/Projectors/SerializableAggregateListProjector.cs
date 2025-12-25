using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using System.Collections.Immutable;
using System.Text.Json;
namespace Sekiban.Pure.Projectors;

public record SerializableAggregateListProjector(
    List<SerializableAggregate> List,
    string AggregateProjectorName,
    string AggregateProjectorVersion)
{
    public static Task<ResultBox<string>> SerializeAggregateList<TAggregateProjector>(
        AggregateListProjector<TAggregateProjector> projector,
        SekibanDomainTypes domainTypes) where TAggregateProjector : IAggregateProjector, new() =>
        ResultBox.WrapTry(async () =>
        {

            var serializedAggregates = projector.Aggregates.Values;
            var serializedAggregateList = new List<SerializableAggregate>();
            foreach (var aggregate in serializedAggregates)
            {
                var serializedAggregate = await SerializableAggregate.CreateFromAsync(
                    aggregate,
                    domainTypes.JsonSerializerOptions);
                serializedAggregateList.Add(serializedAggregate);
            }
            var serializable = new SerializableAggregateListProjector(
                serializedAggregateList,
                typeof(TAggregateProjector).FullName ?? string.Empty,
                new TAggregateProjector().GetVersion());
            return JsonSerializer.Serialize(serializable, domainTypes.JsonSerializerOptions);
        });
    public static Task<ResultBox<AggregateListProjector<TAggregateProjector>>>
        DeserializeAggregateList<TAggregateProjector>(string serialized, SekibanDomainTypes domainTypes)
        where TAggregateProjector : IAggregateProjector, new() =>
        ResultBox.WrapTry(async () =>
        {
            var serializable = JsonSerializer.Deserialize<SerializableAggregateListProjector>(
                serialized,
                domainTypes.JsonSerializerOptions);
            if (serializable == null)
            {
                throw new InvalidOperationException("Failed to deserialize SerializableAggregateListProjector");
            }
            var dictionary = new Dictionary<PartitionKeys, Aggregate>();
            foreach (var aggregate in serializable.List)
            {
                var deserialized = await aggregate.ToAggregateAsync(domainTypes);
                if (deserialized.HasValue)
                    dictionary.Add(deserialized.GetValue().PartitionKeys, deserialized.GetValue());
            }
            return new AggregateListProjector<TAggregateProjector>(dictionary.ToImmutableDictionary());
        });
}
