namespace Sekiban.Addon.Tenant.EventSourcings.Commands;

public interface ITenantCommand
{
    public Guid TenantId { get; init; }
}
