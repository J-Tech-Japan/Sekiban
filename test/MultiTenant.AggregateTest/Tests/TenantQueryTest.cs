using MultiTenant.Domain.Aggregates;
using MultiTenant.Domain.Aggregates.Clients.Commands;
using MultiTenant.Domain.Aggregates.Clients.Queries;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Testing;
namespace MultiTenant.AggregateTests.Tests;

public class TenantQueryTest : UnifiedTest<MultiTenantDependency>
{
    private const string clientName1 = "Tenant Name 1";
    private const string clientName2 = "Tenant Name 1";
    private const string tenantId1 = "tenant-1";
    private const string tenantId2 = "tenant-2";
    [Fact]
    private void QueryTest1()
    {
        RunCommand(new CreateClient { Name = clientName1, TenantId = tenantId1 });
        RunCommand(new CreateClient { Name = clientName2, TenantId = tenantId2 });
        ThenGetQueryResponse(
            new ClientListQuery.Parameter(tenantId1),
            result =>
            {
                Assert.Single(result.Items);
                Assert.Equal(clientName1, result.Items.First().Name);
            });
        ThenGetQueryResponse(
            new ClientListQuery.Parameter(tenantId2),
            result =>
            {
                Assert.Single(result.Items);
                Assert.Equal(clientName2, result.Items.First().Name);
            });
        ThenGetQueryResponse(
            new ClientListQuery.Parameter(IMultiProjectionService.ProjectionAllRootPartitions),
            result => Assert.Equal(2, result.Items.Count()));
    }
}
