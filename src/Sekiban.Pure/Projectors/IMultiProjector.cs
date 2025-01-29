using ResultBoxes;
using Sekiban.Pure.Events;

namespace Sekiban.Pure.Projectors;

public interface IMultiProjector<TMultiAggregatePayload> where TMultiAggregatePayload : notnull
{
    public virtual string GetVersion() => "initial";
    public ResultBox<TMultiAggregatePayload> Project(TMultiAggregatePayload payload, IEvent ev);
    public static abstract TMultiAggregatePayload GenerateInitialPayload();
}