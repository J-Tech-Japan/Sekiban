using FeatureCheck.Domain.Aggregates.TenantUsers;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.SingleProjections;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class ResultWithTenantSpec : AggregateTest<TenantUser, FeatureCheckDependency>
{

    [Fact]
    public void CreateTenantUserTest()
    {
        WhenCommand(new CreateTenantUser("John", "john@example.com", "tenant1"));
        ThenPayloadIs(new TenantUser("John", "john@example.com"));
    }
    [Fact]
    public void CreateTenantUserTestWithDifferentTenant()
    {
        GivenEnvironmentCommand(new CreateTenantUser("John", "john@example.com", "tenant2"));
        // if different tenant, then it should not throw exception
        WhenCommand(new CreateTenantUser("John", "john@example.com", "tenant1"));
        ThenPayloadIs(new TenantUser("John", "john@example.com"));
    }

    [Fact]
    public void CreateTenantUserTestWithSameTenant()
    {
        GivenEnvironmentCommand(new CreateTenantUser("John D", "john@example.com", "tenant1"));
        // if same tenant, then it should throw exception
        WhenCommand(new CreateTenantUser("John", "john@example.com", "tenant1"));
        ThenThrowsAnException();
    }
}
