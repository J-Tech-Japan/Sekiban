using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants.ValueObjects;
using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants;

public record SekibanTenantContents(TenantNameString Name, TenantCodeString Code) : IAggregateContents
{
    public SekibanTenantContents() : this(string.Empty, string.Empty) { }
}
