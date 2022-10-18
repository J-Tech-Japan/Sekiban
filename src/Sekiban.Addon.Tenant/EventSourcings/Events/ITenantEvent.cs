namespace Sekiban.Addon.Tenant.EventSourcings.Events;

public interface ITenantEvent
{
    public Guid TenantId { get; init; }
}
