using Sekiban.Core.Event;
namespace Sekiban.Core.Query.MultipleProjections;

public interface IMultiProjector<TProjectionPayload> : IProjection where TProjectionPayload : IMultiProjectionPayload
{
    void ApplyEvent(IAggregateEvent ev);
    MultiProjectionState<TProjectionPayload> ToState();
    void ApplySnapshot(MultiProjectionState<TProjectionPayload> snapshot);
    /// <summary>
    ///     対象のAggregate名リスト
    ///     Emptyの場合は、全ての集約を対象とする
    /// </summary>
    /// <returns></returns>
    IList<string> TargetAggregateNames();
}
