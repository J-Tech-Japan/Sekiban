namespace Sekiban.EventSourcing.Queries.SingleAggregates;

public interface ISingleAggregateProjectionDtoConvertible<TDto> where TDto : ISingleAggregate
{
    TDto ToDto();
    void ApplySnapshot(TDto snapshot);
}