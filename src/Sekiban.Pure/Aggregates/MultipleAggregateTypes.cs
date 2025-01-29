using ResultBoxes;
using Sekiban.Pure.Exception;

namespace Sekiban.Pure.Aggregates;

public record MultipleAggregateTypes(List<IAggregateTypes> AggregateTypes) : IAggregateTypes
{
    public ResultBox<IAggregate> ToTypedPayload(Aggregate aggregate) =>
        AggregateTypes
            .Select(aggregateTypes => aggregateTypes.ToTypedPayload(aggregate))
            .FirstOrDefault(resultBox => resultBox.IsSuccess)
        ?? ResultBox<IAggregate>.FromException(new SekibanAggregateTypeException("No Aggregate Type Found"));
}