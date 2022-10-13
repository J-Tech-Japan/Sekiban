namespace Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenantMembers;

public record TenantMemberRecord(Guid MemberId, string Code, string Name)
{
}
