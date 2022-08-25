namespace Sekiban.EventSourcing.Addon.Tenant.EventSourcings.Commands;

public interface ITenantCommand
{
    public Guid TenantId { get; init; }
}
