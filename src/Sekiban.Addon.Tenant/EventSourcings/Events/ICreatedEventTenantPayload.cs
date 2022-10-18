using Sekiban.Core.Event;
namespace Sekiban.Addon.Tenant.EventSourcings.Events;

public interface ICreatedEventTenantPayload : ICreatedEventPayload, ITenantEvent
{
}
