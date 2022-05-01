namespace Sekiban.EventSourcing.Queries;

public interface ISingleAggregateProjectionDtoConvertible<TDto>
    where TDto : ISingleAggregate
{
    TDto ToDto();
    void ApplySnapshot(TDto snapshot);
}
