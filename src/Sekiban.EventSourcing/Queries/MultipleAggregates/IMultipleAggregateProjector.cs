namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public interface IMultipleAggregateProjector<TContents> : IProjection where TContents : IMultipleAggregateProjectionContents
{
    void ApplyEvent(IAggregateEvent ev);
    MultipleAggregateProjectionContentsDto<TContents> ToDto();
    void ApplySnapshot(MultipleAggregateProjectionContentsDto<TContents> snapshot);
    /// <summary>
    ///     対象のAggregate名リスト
    ///     Emptyの場合は、全ての集約を対象とする
    /// </summary>
    /// <returns></returns>
    IList<string> TargetAggregateNames();
}
