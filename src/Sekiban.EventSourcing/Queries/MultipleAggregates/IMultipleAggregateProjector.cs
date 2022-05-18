namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public interface IMultipleAggregateProjector<TDto> : IProjection where TDto : IMultipleAggregateProjectionDto
{
    void ApplyEvent(AggregateEvent ev);
    TDto ToDto();
    void ApplySnapshot(TDto snapshot);
    /// <summary>
    ///     対象のAggregate名リスト
    ///     Emptyの場合は、全ての集約を対象とする
    /// </summary>
    /// <returns></returns>
    IList<string> TargetAggregateNames();
}
