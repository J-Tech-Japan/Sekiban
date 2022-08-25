using Sekiban.EventSourcing.Addon.Tenant.EventSourcings.Events;
using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.Addon.Tenant.EventSourcings.Aggregates;

public abstract class TenantAggregateBase<TContents> : TransferableAggregateBase<TContents>, ITenantAggregate
    where TContents : ITenantAggregateContents, new()
{
    protected new void AddAndApplyEvent<TEventPayload>(TEventPayload eventPayload) where TEventPayload : IEventPayload, ITenantEvent
    {
        base.AddAndApplyEvent(eventPayload);
    }
}
