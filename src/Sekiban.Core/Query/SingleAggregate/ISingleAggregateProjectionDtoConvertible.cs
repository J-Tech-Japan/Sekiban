namespace Sekiban.Core.Query.SingleAggregate;

public interface ISingleAggregateProjectionDtoConvertible<TDto> where TDto : ISingleAggregate
{
    TDto ToState();
    void ApplySnapshot(TDto snapshot);
}
