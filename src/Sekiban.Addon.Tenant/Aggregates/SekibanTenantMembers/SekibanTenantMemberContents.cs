using Sekiban.Core.Aggregate;
namespace Sekiban.Addon.Tenant.Aggregates.SekibanTenantMembers;

public record SekibanTenantMemberContents(IReadOnlyCollection<Guid> TenantMemberRecords) : IAggregateContents;
