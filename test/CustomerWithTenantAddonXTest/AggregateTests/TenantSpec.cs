using Sekiban.Addon.Tenant.Aggregates.SekibanTenants;
using Sekiban.Addon.Tenant.Aggregates.SekibanTenants.Commands;
using System;
using Xunit;
namespace CustomerWithTenantAddonXTest.AggregateTests;

public class TenantSpec : AggregateTestBase<SekibanTenant, SekibanTenantContents>
{
    public static Guid TenantId = Guid.NewGuid();
    public static string TenantName = "Sekiban";
    public static string TenantCode = "sekibanTenant";

    [Fact]
    public void CreateTenant()
    {
        WhenCreate(new CreateSekibanTenant(TenantId, TenantName, TenantCode))
            .ThenNotThrowsAnException()
            .ThenContentsIs(new SekibanTenantContents(TenantName, TenantCode));
    }
}
