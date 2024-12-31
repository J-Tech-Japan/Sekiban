using Sekiban.Pure.Events;
namespace Sekiban.Pure.Projectors;

public interface IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev);
    public virtual string GetVersion() => "initial";
}
