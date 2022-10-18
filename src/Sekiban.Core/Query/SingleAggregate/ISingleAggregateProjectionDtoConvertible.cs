namespace Sekiban.Core.Query.SingleAggregate;

public interface ISingleAggregateProjectionDtoConvertible<TDto> where TDto : ISingleAggregate
{
    TDto ToDto();
    void ApplySnapshot(TDto snapshot);
}
