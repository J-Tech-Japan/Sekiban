using Sekiban.Addon.Tenant.EventSourcings.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
namespace Sekiban.Addon.Tenant.EventSourcings.Aggregates;

public abstract class TenantAggregateBase<TContents> : AggregateBase<TContents>, ITenantAggregate where TContents : ITenantAggregateContents, new()
{
    protected new void AddAndApplyEvent<TEventPayload>(TEventPayload eventPayload) where TEventPayload : IEventPayload, ITenantEvent
    {
        base.AddAndApplyEvent(eventPayload);
    }
}
