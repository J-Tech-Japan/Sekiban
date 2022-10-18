using Sekiban.Core.Event;
namespace Sekiban.Addon.Tenant.EventSourcings.Events;

public interface IChangedEventTenantPayload : IChangedEventPayload, ITenantEvent
{
}
