using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants
{
    public record SekibanTenantContents(string Name, string Code) : IAggregateContents
    {
        public SekibanTenantContents() : this(string.Empty, string.Empty) { }
    }
}
