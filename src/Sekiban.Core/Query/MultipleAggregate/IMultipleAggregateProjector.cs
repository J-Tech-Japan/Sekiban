using Sekiban.Core.Event;
namespace Sekiban.Core.Query.MultipleAggregate;

public interface IMultipleAggregateProjector<TProjectionPayload> : IProjection where TProjectionPayload : IMultipleAggregateProjectionPayload
{
    void ApplyEvent(IAggregateEvent ev);
    MultipleAggregateProjectionState<TProjectionPayload> ToState();
    void ApplySnapshot(MultipleAggregateProjectionState<TProjectionPayload> snapshot);
    /// <summary>
    ///     対象のAggregate名リスト
    ///     Emptyの場合は、全ての集約を対象とする
    /// </summary>
    /// <returns></returns>
    IList<string> TargetAggregateNames();
}
