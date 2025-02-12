using ResultBoxes;
using Sekiban.Pure.Events;

namespace Sekiban.Pure.Projectors;

public interface IMultiProjector<TMultiAggregatePayload> : IMultiProjectorCommon where TMultiAggregatePayload : notnull
{
    public ResultBox<TMultiAggregatePayload> Project(TMultiAggregatePayload payload, IEvent ev);
    public static abstract TMultiAggregatePayload GenerateInitialPayload();
}