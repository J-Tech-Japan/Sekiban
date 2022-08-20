using Sekiban.EventSourcing.Aggregates;
namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenantMembers
{
    public record SekibanTenantMemberContents(IReadOnlyCollection<Guid> TenantMemberRecords) : IAggregateContents;
}
