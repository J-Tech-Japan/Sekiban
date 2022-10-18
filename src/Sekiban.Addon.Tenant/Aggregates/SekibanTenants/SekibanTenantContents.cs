using Sekiban.Addon.Tenant.Aggregates.SekibanTenants.ValueObjects;
using Sekiban.Core.Aggregate;
namespace Sekiban.Addon.Tenant.Aggregates.SekibanTenants;

public record SekibanTenantContents(TenantNameString Name, TenantCodeString Code) : IAggregateContents
{
    public SekibanTenantContents() : this(string.Empty, string.Empty) { }
}
