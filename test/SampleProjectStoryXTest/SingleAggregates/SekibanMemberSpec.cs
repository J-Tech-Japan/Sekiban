using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanMembers;
using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanMembers.Commands;
using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants;
using Sekiban.EventSourcing.Aggregates;
using System;
using Xunit;
namespace SampleProjectStoryXTest.SingleAggregates;

public class SekibanMemberSpec : SampleSingleAggregateTestBase<SekibanMember, SekibanMemberContents>
{
    public static Guid MemberId = Guid.NewGuid();
    public static string MemberName = "Sekiban Member";
    public static string MemberEmail = "sekiban@example.com";
    public static string UniqueId = "sekiban";
    [Fact]
    public void CreateMember()
    {
        GivenEnvironmentDtoContents<SekibanTenant, SekibanTenantContents>(
                TenantSpec.TenantId,
                new SekibanTenantContents(TenantSpec.TenantName, TenantSpec.TenantName))
            .WhenCreate(new CreateSekibanMember(TenantSpec.TenantId, MemberId, MemberName, MemberEmail, UniqueId))
            .ThenNotThrowsAnException()
            .ThenState(member => new AggregateDto<SekibanMemberContents>(member, new SekibanMemberContents(MemberName, MemberEmail, UniqueId)));
    }
}
