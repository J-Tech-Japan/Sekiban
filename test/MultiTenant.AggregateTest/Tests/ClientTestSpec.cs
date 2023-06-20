using MultiTenant.Domain.Aggregates;
using MultiTenant.Domain.Aggregates.Clients;
using MultiTenant.Domain.Aggregates.Clients.Commands;
using MultiTenant.Domain.Aggregates.Clients.Queries;
using Sekiban.Testing.SingleProjections;
namespace MultiTenant.AggregateTests.Tests;

public class ClientTestSpec : AggregateTest<ClientPayload, MultiTenantDependency>
{
    private const string clientName = "Tenant Name 1";
    private const string tenantId1 = "tenant-1";
    private const string tenantId2 = "tenant-2";
    [Fact]
    public void CreateClientTest()
    {
        WhenCommand(new CreateClient { Name = clientName, TenantId = tenantId1 }).ThenPayloadIs(new ClientPayload { Name = clientName });
    }

    [Fact]
    public void QueryTest1()
    {
        WhenCommand(new CreateClient { Name = clientName, TenantId = tenantId1 })
            .ThenPayloadIs(new ClientPayload { Name = clientName })
            .ThenGetQueryResponse(new ClientListQuery.Parameter(tenantId1), result => Assert.Single(result.Items));
        ThenGetQueryResponse(new ClientListQuery.Parameter(tenantId2), result => Assert.Empty(result.Items));
    }
}
