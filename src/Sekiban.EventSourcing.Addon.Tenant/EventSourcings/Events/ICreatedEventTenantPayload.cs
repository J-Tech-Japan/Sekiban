using Sekiban.EventSourcing.AggregateEvents;
namespace Sekiban.EventSourcing.Addon.Tenant.EventSourcings.Events;

public interface ICreatedEventTenantPayload : ICreatedEventPayload, ITenantEvent
{
}
