using Sekiban.Core.Events;
namespace Sekiban.Core.Query.MultiProjections;

/// <summary>
///     Multi Projection general interface
///     Developers does not need to implement this interface directly.
/// </summary>
/// <typeparam name="TProjectionPayload"></typeparam>
public interface IMultiProjector<TProjectionPayload> : IMultiProjectionCommon where TProjectionPayload : IMultiProjectionPayloadCommon, new()
{
    /// <summary>
    ///     Check Event should be applied to this projection payload.
    /// </summary>
    /// <param name="ev"></param>
    /// <returns></returns>
    public bool EventShouldBeApplied(IEvent ev);
    /// <summary>
    ///     Apply Event to this projection payload.
    /// </summary>
    /// <param name="ev"></param>
    void ApplyEvent(IEvent ev);
    /// <summary>
    ///     Get Projection State
    /// </summary>
    /// <returns></returns>
    MultiProjectionState<TProjectionPayload> ToState();
    /// <summary>
    ///     Apply Snapshot to this projection payload.
    /// </summary>
    /// <param name="snapshot"></param>
    void ApplySnapshot(MultiProjectionState<TProjectionPayload> snapshot);

    /// <summary>
    ///     Target Aggregate Name List
    ///     If Empty, all aggregates will be targeted.
    /// </summary>
    /// <returns></returns>
    IList<string> TargetAggregateNames();
}
