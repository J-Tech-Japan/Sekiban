using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants;
using Sekiban.EventSourcing.Addon.Tenant.Aggregates.SekibanTenants.Commands;
using System;
using Xunit;
namespace SampleProjectStoryXTest.SingleAggregates;

public class TenantSpec : SampleSingleAggregateTestBase<SekibanTenant, SekibanTenantContents>
{
    public static Guid TenantId = Guid.NewGuid();
    public static string TenantName = "Sekiban";
    public static string TenantCode = "sekibanTenant";

    [Fact]
    public void CreateTenant()
    {
        WhenCreate(new CreateSekibanTenant(TenantId, TenantName, TenantCode))
            .ThenNotThrowsAnException()
            .ThenContents(new SekibanTenantContents(TenantName, TenantCode));
    }
}
